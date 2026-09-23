using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using ProjectLauncher.Core;

namespace ProjectLauncher.Windows;

/// <summary>
/// Persistent Windows pseudo-console session. Synchronous input and output pipes are serviced
/// by different workers; the UI thread never waits on pipe I/O or ClosePseudoConsole.
/// </summary>
public sealed class ConPtySession : IAsyncDisposable
{
    private readonly object _lifecycle = new();
    private readonly BlockingCollection<string> _input = new(256);
    private IntPtr _console;
    private SafeKernelHandle? _process;
    private SafeFileHandle? _inputHandle, _outputHandle;
    private JobObject? _job;
    private Task? _readerTask, _writerTask, _monitorTask, _closeTask;
    private bool _started;
    private volatile bool _closing;
    public bool IsRunning => _started && !_closing;
    public int ProcessId { get; private set; }
    public event Action<string>? Output;
    public event Action<int>? Exited;
    public event Action<string>? Diagnostic;

    public void Start(IReadOnlyList<string> command, string workingDirectory, IReadOnlyDictionary<string, string> environment, int columns = 100, int rows = 30)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) throw new ConfigException(ProjectLauncher.Core.Localization.TextCatalog.Format(ProjectLauncher.Core.Localization.TextCatalog.Language, "Runtime.12"));
        if (command.Count == 0) throw new ConfigException(ProjectLauncher.Core.Localization.TextCatalog.Format(ProjectLauncher.Core.Localization.TextCatalog.Language, "Runtime.13"));
        lock (_lifecycle)
        {
            if (_started || _closing) throw new InvalidOperationException(ProjectLauncher.Core.Localization.TextCatalog.Format(ProjectLauncher.Core.Localization.TextCatalog.Language, "Runtime.14"));
            IntPtr inputRead = IntPtr.Zero, inputWrite = IntPtr.Zero, outputRead = IntPtr.Zero, outputWrite = IntPtr.Zero;
            IntPtr attributes = IntPtr.Zero, environmentBlock = IntPtr.Zero;
            bool attributeListInitialized = false;
            NativeMethods.ProcessInformation processInfo = default;
            try
            {
                if (!NativeMethods.CreatePipe(out inputRead, out inputWrite, IntPtr.Zero, 0)) throw LastError();
                if (!NativeMethods.CreatePipe(out outputRead, out outputWrite, IntPtr.Zero, 0)) throw LastError();
                int result = NativeMethods.CreatePseudoConsole(new(Math.Clamp(columns, 2, 500), Math.Clamp(rows, 2, 300)), inputRead, outputWrite, 0, out _console);
                Marshal.ThrowExceptionForHR(result);
                IntPtr size = IntPtr.Zero;
                NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
                if (size == IntPtr.Zero) throw LastError();
                attributes = Marshal.AllocHGlobal(size);
                if (!NativeMethods.InitializeProcThreadAttributeList(attributes, 1, 0, ref size)) throw LastError();
                attributeListInitialized = true;
                if (!NativeMethods.UpdateProcThreadAttribute(attributes, 0, NativeMethods.PseudoConsoleAttribute, _console, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero)) throw LastError();
                var start = new NativeMethods.StartupInfoEx { AttributeList = attributes };
                start.StartupInfo.Cb = Marshal.SizeOf<NativeMethods.StartupInfoEx>();
                start.StartupInfo.Flags = NativeMethods.StartfUseStdHandles;
                var block = string.Join('\0', environment.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(x => x.Key + "=" + x.Value)) + "\0\0";
                environmentBlock = Marshal.StringToHGlobalUni(block);
                string executable = command[0];
                if (!CommandBuilder.IsExplicitPath(executable)) executable = ProjectEnvironment.FindExecutable(executable, environment) ?? throw new ConfigException(ProjectLauncher.Core.Localization.TextCatalog.Format(ProjectLauncher.Core.Localization.TextCatalog.Language, "Runtime.15") + executable);
                var args = command.ToList(); args[0] = executable;
                if (!NativeMethods.CreateProcess(executable, new StringBuilder(WindowsArguments.Join(args)), IntPtr.Zero, IntPtr.Zero, false,
                    NativeMethods.ExtendedStartupInfoPresent | NativeMethods.CreateUnicodeEnvironment | NativeMethods.CreateSuspended,
                    environmentBlock, workingDirectory, ref start, out processInfo)) throw LastError();
                _job = new JobObject();
                _job.AttachHandle(processInfo.Process); // Bind before resume so descendants cannot escape this initial race.
                _process = new SafeKernelHandle(processInfo.Process); processInfo.Process = IntPtr.Zero;
                ProcessId = checked((int)processInfo.ProcessId);
                _inputHandle = new SafeFileHandle(inputWrite, true); inputWrite = IntPtr.Zero;
                _outputHandle = new SafeFileHandle(outputRead, true); outputRead = IntPtr.Zero;
                _started = true;
                _readerTask = Task.Run(ReadLoop);
                _writerTask = Task.Run(WriteLoop);
                if (NativeMethods.ResumeThread(processInfo.Thread) == uint.MaxValue) throw LastError();
                _monitorTask = Task.Run(MonitorLoop);
            }
            catch
            {
                if (processInfo.Process != IntPtr.Zero) NativeMethods.TerminateProcess(processInfo.Process, 1);
                _job?.Dispose(); _job = null;
                // If startup reached the read workers, keep cleanup off this thread and keep output draining.
                _ = CloseAsync();
                throw;
            }
            finally
            {
                CloseRaw(ref inputRead); CloseRaw(ref inputWrite); CloseRaw(ref outputRead); CloseRaw(ref outputWrite);
                if (processInfo.Thread != IntPtr.Zero) NativeMethods.CloseHandle(processInfo.Thread);
                if (processInfo.Process != IntPtr.Zero) NativeMethods.CloseHandle(processInfo.Process);
                if (attributes != IntPtr.Zero) { if (attributeListInitialized) NativeMethods.DeleteProcThreadAttributeList(attributes); Marshal.FreeHGlobal(attributes); }
                if (environmentBlock != IntPtr.Zero) Marshal.FreeHGlobal(environmentBlock);
            }
        }
    }

    private void ReadLoop()
    {
        try
        {
            using var stream = new FileStream(_outputHandle!, FileAccess.Read, 4096, false);
            using var reader = new StreamReader(stream, new UTF8Encoding(false, false), false, 4096);
            var buffer = new char[4096];
            while (true)
            {
                int length = reader.Read(buffer, 0, buffer.Length); if (length == 0) break;
                Output?.Invoke(new string(buffer, 0, length));
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        { if (!_closing) Diagnostic?.Invoke(ProjectLauncher.Core.Localization.TextCatalog.Format(ProjectLauncher.Core.Localization.TextCatalog.Language, "Runtime.16") + e.Message); }
    }
    private void WriteLoop()
    {
        try
        {
            using var stream = new FileStream(_inputHandle!, FileAccess.Write, 4096, false);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096) { AutoFlush = true };
            foreach (var text in _input.GetConsumingEnumerable())
            {
                if (_closing) break;
                writer.Write(text); writer.Flush();
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        { if (!_closing) Diagnostic?.Invoke(ProjectLauncher.Core.Localization.TextCatalog.Format(ProjectLauncher.Core.Localization.TextCatalog.Language, "Runtime.17") + e.Message); }
    }
    private void MonitorLoop()
    {
        var process = _process;
        if (process is null || process.IsInvalid) return;
        NativeMethods.WaitForSingleObject(process.DangerousGetHandle(), NativeMethods.Infinite);
        NativeMethods.GetExitCodeProcess(process.DangerousGetHandle(), out uint code);
        Exited?.Invoke(unchecked((int)code));
        _ = CloseAsync();
    }
    public void Write(string text)
    {
        if (!IsRunning) return;
        if (text.Length > 1_048_576) throw new ConfigException(ProjectLauncher.Core.Localization.TextCatalog.Format(ProjectLauncher.Core.Localization.TextCatalog.Language, "Runtime.18"));
        try { if (!_input.TryAdd(text)) throw new ConfigException(ProjectLauncher.Core.Localization.TextCatalog.Format(ProjectLauncher.Core.Localization.TextCatalog.Language, "Runtime.19")); }
        catch (InvalidOperationException) { if (!_closing) throw; }
    }
    public void Resize(int columns, int rows)
    {
        lock (_lifecycle)
        {
            if (_closing || _console == IntPtr.Zero) return;
            Marshal.ThrowExceptionForHR(NativeMethods.ResizePseudoConsole(_console, new(Math.Clamp(columns, 2, 500), Math.Clamp(rows, 2, 300))));
        }
    }
    public Task CloseAsync()
    {
        lock (_lifecycle)
        {
            if (_closeTask is not null) return _closeTask;
            _closing = true; _input.CompleteAdding();
            _closeTask = Task.Run(async () =>
            {
                _job?.Dispose(); _job = null;
                IntPtr console;
                lock (_lifecycle) { console = _console; _console = IntPtr.Zero; }
                // ReadLoop stays active while ClosePseudoConsole drains the remaining output.
                if (console != IntPtr.Zero) NativeMethods.ClosePseudoConsole(console);
                _inputHandle?.Dispose(); _outputHandle?.Dispose();
                var tasks = new[] { _readerTask, _writerTask, _monitorTask }.Where(t => t is not null).Cast<Task>().ToArray();
                try { await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(8)); } catch (TimeoutException) { }
                _process?.Dispose(); _process = null;
            });
            return _closeTask;
        }
    }
    public ValueTask DisposeAsync() => new(CloseAsync());
    private static Win32Exception LastError() => new(Marshal.GetLastWin32Error());
    private static void CloseRaw(ref IntPtr handle) { if (handle != IntPtr.Zero) { NativeMethods.CloseHandle(handle); handle = IntPtr.Zero; } }
}
