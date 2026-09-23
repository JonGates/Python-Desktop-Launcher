using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ProjectLauncher.Core;

public interface IProcessLifetime : IDisposable { void Attach(Process process); }

public sealed class ProcessRunner : IDisposable
{
    private readonly Func<IProcessLifetime>? _lifetimeFactory;
    private readonly object _gate = new();
    private Process? _process;
    private CancellationTokenSource? _stop;
    private bool _running;
    public bool IsRunning { get { lock (_gate) return _running; } }
    public event Action<string>? Output;
    public ProcessRunner(Func<IProcessLifetime>? lifetimeFactory = null) => _lifetimeFactory = lifetimeFactory;

    public async Task<ProcessResult> RunAsync(CommandPlan command, string cwd, Dictionary<string, string> environment, CancellationToken cancellation)
    {
        CancellationTokenSource stop;
        lock (_gate)
        {
            if (_running) throw new InvalidOperationException("该运行器已经有任务在执行。");
            _running = true; _stop = stop = new CancellationTokenSource();
        }
        using var timeout = new CancellationTokenSource();
        if (command.TimeoutSeconds > 0) timeout.CancelAfter(TimeSpan.FromSeconds(command.TimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, timeout.Token, stop.Token);
        var clock = Stopwatch.StartNew();
        Process? process = null; IProcessLifetime? lifetime = null;
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            var executionEnvironment = environment.Count == 0 ? ProjectEnvironment.CleanEnvironment() : new Dictionary<string, string>(environment, StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in command.Environment) executionEnvironment[key] = value;
            string executable = ProjectEnvironment.ResolveExecutable(command.Argv[0], cwd, executionEnvironment);
            var start = new ProcessStartInfo
            {
                FileName = executable, WorkingDirectory = cwd, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false, false), StandardErrorEncoding = new UTF8Encoding(false, false)
            };
            foreach (var arg in command.Argv.Skip(1)) start.ArgumentList.Add(arg);
            start.Environment.Clear();
            foreach (var (key, value) in executionEnvironment) start.Environment[key] = value;
            process = new Process { StartInfo = start };
            if (!process.Start()) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ProcessRunner.1", [command.Argv[0]]));
            lock (_gate) _process = process;
            process.StandardInput.Close(); // GUI actions are noninteractive. Use a project terminal for prompts.
            if (_lifetimeFactory is not null)
            {
                lifetime = _lifetimeFactory();
                try { lifetime.Attach(process); }
                catch (Exception e) { Output?.Invoke("[启动器] Job Object 绑定失败，将使用进程树终止兜底：" + e.Message + "\n"); lifetime.Dispose(); lifetime = null; }
            }
            var outputTask = PumpAsync(process.StandardOutput, new StreamingRedactor(command.Secrets));
            var errorTask = PumpAsync(process.StandardError, new StreamingRedactor(command.Secrets));
            bool cancelled = false;
            try { await process.WaitForExitAsync(linked.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                cancelled = true; Kill(process); lifetime?.Dispose(); lifetime = null;
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
            }
            // Close job-owned descendants even if the parent exits first; inherited output pipes must not hang forever.
            lifetime?.Dispose(); lifetime = null;
            try { await Task.WhenAll(outputTask, errorTask).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
            catch (TimeoutException) { Output?.Invoke("\n[启动器] 输出管道未及时关闭，已停止等待。\n"); }
            return new(process.ExitCode, cancelled || cancellation.IsCancellationRequested || stop.IsCancellationRequested,
                timeout.IsCancellationRequested, clock.Elapsed);
        }
        catch (OperationCanceledException) { return new(-1, true, timeout.IsCancellationRequested, clock.Elapsed); }
        catch (System.ComponentModel.Win32Exception e) { throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ProcessRunner.2", [e.Message]), e); }
        finally
        {
            if (process is not null) Kill(process);
            lifetime?.Dispose(); process?.Dispose();
            lock (_gate) { _process = null; _running = false; _stop = null; }
            stop.Dispose();
        }
    }

    private async Task PumpAsync(StreamReader reader, StreamingRedactor redactor)
    {
        var buffer = new char[4096];
        try
        {
            while (true)
            {
                var count = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false);
                if (count == 0) break;
                var text = redactor.Push(new string(buffer, 0, count));
                if (text.Length > 0) Output?.Invoke(text);
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException) { }
        var tail = redactor.Finish(); if (tail.Length > 0) Output?.Invoke(tail);
    }

    public void Stop()
    {
        lock (_gate) { _stop?.Cancel(); }
    }
    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
    }
    public void Dispose() { Stop(); }
}

public sealed class StreamingRedactor
{
    private readonly string[] _secrets;
    private readonly int _tail;
    private string _pending = "";
    public StreamingRedactor(IEnumerable<string> secrets)
    {
        _secrets = secrets.Where(s => s.Length > 0).Distinct().OrderByDescending(s => s.Length).ToArray();
        _tail = Math.Max(0, _secrets.Select(s => s.Length).DefaultIfEmpty(1).Max() - 1);
    }
    public string Push(string text)
    {
        _pending += text;
        int cut = Math.Max(0, _pending.Length - _tail);
        // If an entire match straddles the emit boundary, keep the whole match for the next push.
        foreach (var secret in _secrets)
        {
            int at = 0;
            while ((at = _pending.IndexOf(secret, at, StringComparison.Ordinal)) >= 0)
            {
                if (at < cut && at + secret.Length > cut) cut = at;
                at++;
            }
        }
        var emit = _pending[..cut]; _pending = _pending[cut..]; return Redact(emit);
    }
    public string Finish() { var text = Redact(_pending); _pending = ""; return text; }
    private string Redact(string text) { foreach (var secret in _secrets) text = text.Replace(secret, "[REDACTED]", StringComparison.Ordinal); return text; }
}

public sealed record ProgressMessage(double Percent, string Message)
{
    public static ProgressMessage? TryParse(string line)
    {
        const string marker = "@@launcher:";
        if (!line.StartsWith(marker, StringComparison.Ordinal) || line.Length > 65536) return null;
        try
        {
            using var doc = JsonDocument.Parse(line[marker.Length..]);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("progress", out var progress) || progress.ValueKind != JsonValueKind.Number || !progress.TryGetDouble(out var current)) return null;
            double total = 100;
            if (root.TryGetProperty("total", out var t) && (t.ValueKind != JsonValueKind.Number || !t.TryGetDouble(out total))) return null;
            if (total <= 0 || !double.IsFinite(total) || !double.IsFinite(current)) return null;
            var message = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() ?? "" : "";
            return new(Math.Clamp(current / total * 100, 0, 100), message);
        }
        catch (JsonException) { return null; }
    }
}
