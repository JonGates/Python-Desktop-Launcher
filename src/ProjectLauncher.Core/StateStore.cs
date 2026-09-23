using System.Text;
using System.Text.Json;

namespace ProjectLauncher.Core;

public sealed class UserState
{
    public string Theme { get; set; } = "dark";
    public string LastAction { get; set; } = "";
    public string TrustedHash { get; set; } = "";
    public bool ShowAdvanced { get; set; }
    public double WindowWidth { get; set; } = 1360;
    public double WindowHeight { get; set; } = 900;
    public Dictionary<string, object?> Values { get; set; } = new();
    public Dictionary<string, Dictionary<string, object?>> Presets { get; set; } = new();
    public List<RunHistory> History { get; set; } = [];
}

public sealed class RunHistory
{
    public DateTimeOffset Started { get; set; }
    public string ActionId { get; set; } = "";
    public string ActionLabel { get; set; } = "";
    public string Command { get; set; } = "";
    public int ExitCode { get; set; }
    public string Status { get; set; } = "";
    public double DurationSeconds { get; set; }
    public string LogPath { get; set; } = "";
    public override string ToString() => $"{Started.LocalDateTime:MM-dd HH:mm}   {ActionLabel}   {Status}   {DurationSeconds:0.0}s";
}

public static class StateStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public static UserState Load(string directory, LauncherConfig config)
    {
        var path = Path.Combine(directory, "user.json");
        if (!File.Exists(path)) return new();
        try
        {
            if (new FileInfo(path).Length > 4_194_304) throw new JsonException("状态文件过大。");
            var state = JsonSerializer.Deserialize<UserState>(File.ReadAllText(path), Options) ?? new();
            Sanitize(state, config); return state;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            // Keep the damaged original for investigation instead of silently deleting it.
            try { File.Copy(path, path + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), false); } catch { }
            return new();
        }
    }
    public static void Save(string directory, UserState state, LauncherConfig config)
    {
        Sanitize(state, config);
        AtomicFile.Write(Path.Combine(directory, "user.json"), JsonSerializer.Serialize(state, Options));
    }
    public static void Sanitize(UserState state, LauncherConfig config)
    {
        state.Values ??= new(); state.Presets ??= new(); state.History ??= [];
        var safe = config.Parameters.Where(p => !p.IsSecret).Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        Clean(state.Values, safe);
        foreach (var (name, preset) in state.Presets.ToList())
        {
            if (preset is null) { state.Presets.Remove(name); continue; }
            Clean(preset, safe);
        }
        if (state.History.Count > 200) state.History = state.History.TakeLast(200).ToList();
        if (state.Presets.Count > 100) state.Presets = state.Presets.Take(100).ToDictionary(p => p.Key, p => p.Value);
        if (state.Theme is not ("dark" or "light")) state.Theme = "dark";
        if (!double.IsFinite(state.WindowWidth)) state.WindowWidth = 1360;
        if (!double.IsFinite(state.WindowHeight)) state.WindowHeight = 900;
        state.WindowWidth = Math.Clamp(state.WindowWidth, 1060, 3840);
        state.WindowHeight = Math.Clamp(state.WindowHeight, 700, 2160);
    }
    private static void Clean(Dictionary<string, object?> values, HashSet<string> allowed)
    {
        foreach (var key in values.Keys.ToList())
        {
            if (!allowed.Contains(key)) values.Remove(key);
            else values[key] = ValueCodec.Unwrap(values[key]);
        }
    }
}

public sealed class BoundedLogFile : IDisposable
{
    private readonly object _gate = new();
    private readonly StreamWriter _writer;
    private long _written;
    private bool _truncated;
    private bool _disposed;
    public string Path { get; }
    private const long Limit = 10 * 1024 * 1024;
    public BoundedLogFile(string stateDirectory, string actionId)
    {
        var directory = System.IO.Path.Combine(stateDirectory, "logs"); Directory.CreateDirectory(directory);
        Path = System.IO.Path.Combine(directory, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{actionId}-{Guid.NewGuid().ToString("N")[..6]}.log");
        _writer = new StreamWriter(new FileStream(Path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
        // Retain at most 100 prior logs. Only owned .log files in this directory are touched.
        foreach (var file in new DirectoryInfo(directory).GetFiles("*.log").OrderByDescending(f => f.LastWriteTimeUtc).Skip(100))
            try { file.Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    public void Append(string text)
    {
        lock (_gate)
        {
            if (_disposed || _truncated) return;
            _written += Encoding.UTF8.GetByteCount(text);
            if (_written > Limit) { _writer.WriteLine("\n[启动器] 单次日志达到 10 MB，停止落盘。任务仍继续执行。"); _truncated = true; return; }
            _writer.Write(text);
        }
    }
    public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; _writer.Dispose(); } }
}
