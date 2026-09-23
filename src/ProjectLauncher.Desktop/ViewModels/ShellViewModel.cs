using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using ProjectLauncher.Core;
using ProjectLauncher.Desktop.Services;
using ProjectLauncher.Windows;

namespace ProjectLauncher.Desktop.ViewModels;

public sealed class ShellViewModel : ObservableObject, IDisposable
{
    public ConfigSnapshot Snapshot { get; private set; }
    public LauncherConfig Config => Snapshot.Config;
    public ProjectEnvironment Runtime { get; private set; }
    public UserState State { get; private set; }
    public ObservableCollection<RunHistory> History { get; } = [];
    public string ProjectName => Config.App.Name;
    public string ProjectDescription => Config.App.Description;
    public string ProjectVersion => "v" + Config.App.Version;
    public string ProjectRoot => Runtime.Root;
    public string RuntimeLabel => Config.Runtime.Mode.ToUpperInvariant() + "  /  " + Config.Runtime.Venv;
    public string EnvironmentBadge => Runtime.Exists ? "项目环境已定位" : "待初始化项目环境";
    public string AppVersion => "2.0  /  C# PREVIEW";
    private bool _busy;
    public bool Busy { get => _busy; private set { if (Set(ref _busy, value)) { Raise(nameof(CanRun)); Raise(nameof(CanEdit)); } } }
    public bool CanRun => !Busy;
    private string _activeActionId = "";
    public string ActiveActionId { get => _activeActionId; private set => Set(ref _activeActionId, value); }
    public bool CanEdit => !Busy && OpenTerminalCount == 0;
    private int _openTerminalCount;
    public int OpenTerminalCount { get => _openTerminalCount; set { if (Set(ref _openTerminalCount, value)) Raise(nameof(CanEdit)); } }
    private string _status = "准备就绪";
    public string Status { get => _status; private set => Set(ref _status, value); }
    private string _elapsed = "00:00";
    public string Elapsed { get => _elapsed; private set => Set(ref _elapsed, value); }
    private double _progress;
    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    private bool _indeterminate;
    public bool IsIndeterminate { get => _indeterminate; private set => Set(ref _indeterminate, value); }
    private string _logText = "";
    public string LogText { get => _logText; private set => Set(ref _logText, value); }
    private string _notification = "";
    public string Notification { get => _notification; private set { Set(ref _notification, value); Raise(nameof(HasNotification)); } }
    public bool HasNotification => Notification.Length > 0;
    public string NotificationDetail { get; private set; } = "";
    private string _notificationSource = "";
    private string _environmentVersion = "尚未检查";
    public string EnvironmentVersion { get => _environmentVersion; private set => Set(ref _environmentVersion, value); }
    private string _environmentDetail = "解释器检查会实际启动项目 Python；依赖安装由你手动确认。";
    public string EnvironmentDetail { get => _environmentDetail; private set => Set(ref _environmentDetail, value); }
    public event Action? ConfigurationChanged;
    public event Action<string>? NavigationRequested;
    private readonly ConcurrentQueue<string> _logQueue = new();
    private readonly DispatcherTimer _timer;
    private readonly StringBuilder _lineBuffer = new();
    private readonly Stopwatch _clock = new();
    private ProcessRunner? _runner;
    private CancellationTokenSource? _operation;
    private BoundedLogFile? _logFile;
    private int _pendingCharacters;
    private bool _droppedUiOutput;
    private bool _diskLogFailed;

    public ShellViewModel(ConfigSnapshot snapshot)
    {
        Snapshot = snapshot; Runtime = new(Config, Snapshot.Path); State = StateStore.Load(Runtime.StateDirectory, Config);
        RefreshHistory();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) => FlushLog(); _timer.Start();
    }
    public void Navigate(string page) => NavigationRequested?.Invoke(page);
    public void Notify(string message) => Notify(message, "general");
    public void Notify(string message, string source)
    {
        if (!Application.Current.Dispatcher.CheckAccess()) { Application.Current.Dispatcher.InvokeAsync(() => Notify(message, source)); return; }
        _notificationSource = source;
        NotificationDetail = message;
        Notification = message.Length <= 240 ? message : message[..240] + "…";
    }
    public void DismissNotification() => Notification = "";
    public void ClearNotification(string source) { if (_notificationSource == source) DismissNotification(); }
    public void SaveState()
    {
        try { StateStore.Save(Runtime.StateDirectory, State, Config); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Notify("无法保存当前用户设置。请将项目放在可写目录：" + e.Message); }
    }
    public bool ConfirmTrust(Window owner)
    {
        if (!File.Exists(Snapshot.Path) || ConfigStore.Hash(File.ReadAllBytes(Snapshot.Path)) != Snapshot.Hash)
        { Notify("配置文件已在磁盘上发生变化。请先重新加载配置，再执行任务。"); return false; }
        if (State.TrustedHash == Snapshot.Hash) return true;
        if (!Dialogs.Confirm(owner, "信任这个项目的启动配置？", $"项目：{ProjectName}\n目录：{ProjectRoot}\n\n启动配置能够运行本机命令、Python 文件和依赖安装。请只运行你理解并信任的项目。启动器不是安全沙箱。\n\n本次确认只记住这份配置的内容哈希；配置更改后会重新确认。", "信任此配置")) return false;
        State.TrustedHash = Snapshot.Hash; SaveState(); return true;
    }
    public async Task RunActionAsync(Window owner, string actionId, Dictionary<string, object?> values)
    {
        if (Busy) return;
        try
        {
            if (!ConfirmTrust(owner)) return;
            var action = Config.Actions.First(a => a.Id == actionId);
            var command = CommandBuilder.Build(Config, actionId, values, Runtime.Root, Runtime.RequirePython());
            State.Values = new(values); State.LastAction = actionId; SaveState();
            ActiveActionId = action.Id;
            await ExecuteAsync([command], action.Id, action.Label, Runtime.ExecutionEnvironment());
        }
        catch (Exception e) { Notify(e.Message); }
        finally { ActiveActionId = ""; }
    }
    public async Task InitializeAsync(Window owner)
    {
        if (Busy) return;
        if (OpenTerminalCount > 0) { Notify("请先关闭项目终端和本壳打开的系统终端，再修改项目环境。"); return; }
        try
        {
            if (!ConfirmTrust(owner)) return;
            var plans = Runtime.InitializationPlan();
            if (plans.Count == 0) { await ProbeAsync(owner); return; }
            var preview = string.Join("\n\n", plans.Select(p => p.Preview));
            if (!Dialogs.Confirm(owner, "创建 / 同步项目环境", preview + "\n\n这些命令可能联网下载 Python / 依赖，并在项目内创建或更新 .venv；uv sync 会按项目依赖状态同步包。启动器不会删除未知环境目录或改写业务源码。", "执行初始化")) return;
            await ExecuteAsync(plans, "environment", "初始化 / 同步环境", Runtime.InitializationEnvironment());
            Raise(nameof(EnvironmentBadge));
            if (Runtime.Exists) Notify("环境操作结束。请点击「检查解释器」验证 sys.prefix，再尝试运行任务。");
        }
        catch (Exception e) { Notify(e.Message); }
    }
    public async Task ProbeAsync(Window owner)
    {
        if (Busy) return;
        try
        {
            if (!ConfirmTrust(owner)) return;
            Busy = true; Status = "正在检查项目解释器"; _operation = new();
            var probe = await Runtime.ProbeAsync(_operation.Token);
            EnvironmentVersion = "Python " + probe.Version;
            EnvironmentDetail = $"解释器：{probe.Executable}\n虚拟环境：{probe.Prefix}\n基础解释器：{probe.BasePrefix}\npip：{(probe.HasPip ? "可用" : "未安装（uv 项目不强制需要 pip）")}\n\n这项检查验证了解释器归属，不代表所有业务依赖均已满足。";
            Status = "解释器检查通过"; Raise(nameof(EnvironmentBadge)); Notify("项目解释器检查通过，sys.prefix 与配置环境一致。");
        }
        catch (Exception e) { EnvironmentVersion = "检查未通过"; EnvironmentDetail = e.Message; Status = "解释器检查失败"; Notify(e.Message); }
        finally { Busy = false; _operation?.Dispose(); _operation = null; }
    }
    private async Task ExecuteAsync(List<CommandPlan> commands, string actionId, string label, Dictionary<string, string> environment)
    {
        if (Busy) return;
        Busy = true; Progress = 0; IsIndeterminate = true; Status = "正在执行 · " + label;
        _clock.Restart(); Elapsed = "00:00"; _operation = new(); _diskLogFailed = false;
        ClearLog();
        var history = new RunHistory { Started = DateTimeOffset.Now, ActionId = actionId, ActionLabel = label, Command = string.Join("\n", commands.Select(c => c.Preview)), Status = "失败", ExitCode = -1 };
        try
        {
            _logFile = new(Runtime.StateDirectory, actionId); history.LogPath = _logFile.Path;
            QueueLog($"Project Launcher · {label}\n工作目录：{Runtime.Root}\n开始时间：{history.Started:yyyy-MM-dd HH:mm:ss}\n\n");
            ProcessResult? result = null;
            for (int i = 0; i < commands.Count; i++)
            {
                _operation.Token.ThrowIfCancellationRequested();
                QueueLog($"> {commands[i].Preview}\n");
                using var runner = new ProcessRunner(() => new JobObject()); _runner = runner;
                runner.Output += QueueLog;
                result = await runner.RunAsync(commands[i], Runtime.Root, environment, _operation.Token);
                QueueLog($"\n[退出码 {result.ExitCode}]\n");
                if (result.ExitCode != 0 || result.Cancelled || result.TimedOut) break;
            }
            history.ExitCode = result?.ExitCode ?? 0;
            history.Status = result?.TimedOut == true ? "超时" : result?.Cancelled == true ? "已停止" : history.ExitCode == 0 ? "成功" : "失败";
            Status = history.Status == "成功" ? "执行完成" : $"{history.Status} · 退出码 {history.ExitCode}";
            if (history.Status == "成功") Progress = 100;
        }
        catch (OperationCanceledException) { history.Status = "已停止"; Status = "任务已停止"; }
        catch (Exception e) { QueueLog("\n[启动器错误] " + e.Message + "\n"); Notify(e.Message); Status = "执行失败"; }
        finally
        {
            _clock.Stop(); history.DurationSeconds = _clock.Elapsed.TotalSeconds;
            QueueLog($"\n── {history.Status} · {history.DurationSeconds:0.0} 秒 ──\n"); FlushLog();
            _logFile?.Dispose(); _logFile = null; _runner = null; _operation.Dispose(); _operation = null;
            IsIndeterminate = false; Busy = false;
            State.History.Add(history); SaveState(); RefreshHistory(); Raise(nameof(EnvironmentBadge));
        }
    }
    private void QueueLog(string text)
    {
        if (!_diskLogFailed)
        {
            try { _logFile?.Append(text); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _diskLogFailed = true; Notify("日志落盘失败，任务继续运行：" + e.Message); }
        }
        if (Interlocked.Add(ref _pendingCharacters, text.Length) > 1_000_000)
        { Interlocked.Add(ref _pendingCharacters, -text.Length); _droppedUiOutput = true; return; }
        _logQueue.Enqueue(text);
    }
    private void FlushLog()
    {
        if (_clock.IsRunning) Elapsed = _clock.Elapsed.TotalHours >= 1 ? _clock.Elapsed.ToString(@"hh\:mm\:ss") : _clock.Elapsed.ToString(@"mm\:ss");
        var batch = new StringBuilder();
        while (batch.Length < 100_000 && _logQueue.TryDequeue(out var text)) { Interlocked.Add(ref _pendingCharacters, -text.Length); batch.Append(text); }
        if (_droppedUiOutput) { batch.Append("\n[启动器] 输出速度过快，部分界面输出已省略；磁盘日志独立保存（每次上限 10 MB）。\n"); _droppedUiOutput = false; }
        if (batch.Length == 0) return;
        var value = LogText + batch;
        LogText = value.Length > 240_000 ? "[仅显示最近输出，完整记录见日志文件]\n" + value[^220_000..] : value;
        foreach (char c in batch.ToString())
        {
            if (c == '\n')
            {
                var protocol = ProgressMessage.TryParse(_lineBuffer.ToString().TrimEnd('\r')); _lineBuffer.Clear();
                if (protocol is not null && Busy) { Progress = protocol.Percent; IsIndeterminate = false; if (protocol.Message.Length > 0) Status = protocol.Message; }
            }
            else if (_lineBuffer.Length < 65536) _lineBuffer.Append(c);
        }
    }
    public void ClearLog()
    {
        while (_logQueue.TryDequeue(out var text)) Interlocked.Add(ref _pendingCharacters, -text.Length);
        LogText = ""; _lineBuffer.Clear();
    }
    public void Stop() { _operation?.Cancel(); _runner?.Stop(); Status = "正在停止任务…"; }
    public void ApplyConfiguration(LauncherConfig draft)
    {
        if (!CanEdit) throw new ConfigException("请先停止任务并关闭所有项目终端，再保存配置。");
        Snapshot = ConfigStore.Save(Snapshot.Path, draft, Snapshot.Hash);
        Runtime = new(Config, Snapshot.Path); State.TrustedHash = ""; StateStore.Sanitize(State, Config); SaveState();
        EnvironmentVersion = "尚未检查"; EnvironmentDetail = "配置已更新，请重新检查项目解释器。";
        RaiseAllConfiguration(); ConfigurationChanged?.Invoke(); Notify("配置已保存并应用。上一版内容备份在 launcher.yaml.bak。");
    }
    public void ReloadConfiguration()
    {
        if (!CanEdit) throw new ConfigException("请先停止任务并关闭所有项目终端，再重新加载配置。");
        Snapshot = ConfigStore.Load(Snapshot.Path); Runtime = new(Config, Snapshot.Path); StateStore.Sanitize(State, Config);
        RaiseAllConfiguration(); ConfigurationChanged?.Invoke(); Notify("已从磁盘重新加载 launcher.yaml。");
    }
    private void RaiseAllConfiguration()
    {
        foreach (var name in new[] { nameof(Config), nameof(ProjectName), nameof(ProjectDescription), nameof(ProjectVersion), nameof(ProjectRoot), nameof(RuntimeLabel), nameof(EnvironmentBadge) }) Raise(name);
    }
    private void RefreshHistory() { History.Clear(); foreach (var item in State.History.AsEnumerable().Reverse()) History.Add(item); }
    public void Dispose() { _timer.Stop(); _operation?.Cancel(); _runner?.Stop(); _logFile?.Dispose(); }
}
