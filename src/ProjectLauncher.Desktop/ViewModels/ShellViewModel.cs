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
    public bool EnvironmentAvailable => Runtime.Exists;
    public string EnvironmentBadge => EnvironmentAvailable ? L.Text("Text.112") : L.Text("Text.113");
    public string AppVersion => "2.0  /  C# PREVIEW";
    private bool _busy;
    public bool Busy { get => _busy; private set { if (Set(ref _busy, value)) { Raise(nameof(CanRun)); Raise(nameof(CanEdit)); } } }
    public bool CanRun => !Busy;
    private string _activeActionId = "";
    public string ActiveActionId { get => _activeActionId; private set => Set(ref _activeActionId, value); }
    public bool CanEdit => !Busy && OpenTerminalCount == 0;
    private int _openTerminalCount;
    public int OpenTerminalCount { get => _openTerminalCount; set { if (Set(ref _openTerminalCount, value)) Raise(nameof(CanEdit)); } }
    private Func<string>? _statusRender = () => L.Text("Text.114");
    private string _status = L.Text("Text.114");
    public string Status { get => _status; private set { _statusRender = null; Set(ref _status, value); } }
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
    private Func<string>? _notificationRender;
    private string _notificationSource = "";
    private string _environmentVersion = L.Text("Text.115");
    private Func<string>? _environmentVersionRender = () => L.Text("Text.115");
    private Func<string>? _environmentDetailRender = () => L.Text("Text.116");
    public string EnvironmentVersion { get => _environmentVersion; private set => Set(ref _environmentVersion, value); }
    private string _environmentDetail = L.Text("Text.116");
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
        System.ComponentModel.PropertyChangedEventManager.AddHandler(LocalizationService.Current, LanguageChanged, nameof(LocalizationService.Language));
        RefreshHistory();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) => FlushLog(); _timer.Start();
    }
    public void Navigate(string page) => NavigationRequested?.Invoke(page);
    public void Notify(string message) => Notify(message, "general");
    public void Notify(string message, string source)
    {
        if (!Application.Current.Dispatcher.CheckAccess()) { Application.Current.Dispatcher.InvokeAsync(() => Notify(message, source)); return; }
        _notificationRender = null;
        _notificationSource = source;
        NotificationDetail = message;
        Notification = message.Length <= 240 ? message : message[..240] + "…";
    }
    public void Notify(Exception error) => NotifyLocalized(() => error.Message);
    public void NotifyLocalized(Func<string> render, string source = "general")
    {
        if (!Application.Current.Dispatcher.CheckAccess()) { Application.Current.Dispatcher.InvokeAsync(() => NotifyLocalized(render, source)); return; }
        Notify(render(), source); _notificationRender = render;
    }
    private void SetStatus(Func<string> render) { _statusRender = render; Set(ref _status, render(), nameof(Status)); }
    private void LanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_statusRender is not null) Set(ref _status, _statusRender(), nameof(Status));
        if (_environmentVersionRender is not null) EnvironmentVersion = _environmentVersionRender();
        if (_environmentDetailRender is not null) EnvironmentDetail = _environmentDetailRender();
        if (_notificationRender is not null && HasNotification) {
            var render = _notificationRender; NotifyLocalized(render, _notificationSource);
        }
        Raise(nameof(EnvironmentBadge));
        if (LocalizationService.Current.PreferenceError is string error) Notify(error);
    }
    public void DismissNotification() => Notification = "";
    public void ClearNotification(string source) { if (_notificationSource == source) DismissNotification(); }
    public void SaveState()
    {
        try { StateStore.Save(Runtime.StateDirectory, State, Config); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { NotifyLocalized(() => L.Text("Text.117") + e.Message); }
    }
    public bool ConfirmTrust(Window owner)
    {
        if (!File.Exists(Snapshot.Path) || ConfigStore.Hash(File.ReadAllBytes(Snapshot.Path)) != Snapshot.Hash)
        { NotifyLocalized(() => L.Text("Text.118")); return false; }
        if (State.TrustedHash == Snapshot.Hash) return true;
        if (!Dialogs.Confirm(owner, L.Text("Text.119"), L.Text("Trust.Body", ProjectName, ProjectRoot), L.Text("Text.120"))) return false;
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
        catch (Exception e) { Notify(e); }
        finally { ActiveActionId = ""; }
    }
    public async Task InitializeAsync(Window owner)
    {
        if (Busy) return;
        if (OpenTerminalCount > 0) { NotifyLocalized(() => L.Text("Text.121")); return; }
        try
        {
            if (!ConfirmTrust(owner)) return;
            var plans = Runtime.InitializationPlan();
            if (plans.Count == 0) { await ProbeAsync(owner); return; }
            var preview = string.Join("\n\n", plans.Select(p => p.Preview));
            if (!Dialogs.Confirm(owner, L.Text("Text.122"), preview + L.Text("Text.123"), L.Text("Text.124"))) return;
            await ExecuteAsync(plans, "environment", L.Text("Text.125"), Runtime.InitializationEnvironment());
            Raise(nameof(EnvironmentBadge)); Raise(nameof(EnvironmentAvailable));
            if (Runtime.Exists) NotifyLocalized(() => L.Text("Text.126"));
        }
        catch (Exception e) { Notify(e); }
    }
    public async Task ProbeAsync(Window owner)
    {
        if (Busy) return;
        try
        {
            if (!ConfirmTrust(owner)) return;
            Busy = true; SetStatus(() => L.Text("Text.127")); _operation = new();
            var probe = await Runtime.ProbeAsync(_operation.Token);
            _environmentVersionRender = null; EnvironmentVersion = "Python " + probe.Version;
            _environmentDetailRender = () => L.Text("Probe.Detail", probe.Executable, probe.Prefix, probe.BasePrefix, L.Text(probe.HasPip ? "Text.128" : "Text.129"));
            EnvironmentDetail = _environmentDetailRender();
            SetStatus(() => L.Text("Text.130")); Raise(nameof(EnvironmentBadge)); Raise(nameof(EnvironmentAvailable)); NotifyLocalized(() => L.Text("Text.131"));
        }
        catch (Exception e) { _environmentVersionRender = () => L.Text("Text.132"); _environmentDetailRender = () => e.Message; EnvironmentVersion = _environmentVersionRender(); EnvironmentDetail = _environmentDetailRender(); SetStatus(() => L.Text("Text.133")); Notify(e); }
        finally { Busy = false; _operation?.Dispose(); _operation = null; }
    }
    private async Task ExecuteAsync(List<CommandPlan> commands, string actionId, string label, Dictionary<string, string> environment)
    {
        if (Busy) return;
        Busy = true; Progress = 0; IsIndeterminate = true; SetStatus(() => L.Text("Text.134") + label);
        _clock.Restart(); Elapsed = "00:00"; _operation = new(); _diskLogFailed = false;
        ClearLog();
        var history = new RunHistory { Started = DateTimeOffset.Now, ActionId = actionId, ActionLabel = label, Command = string.Join("\n", commands.Select(c => c.Preview)), Status = L.Text("Text.135"), ExitCode = -1 };
        try
        {
            _logFile = new(Runtime.StateDirectory, actionId); history.LogPath = _logFile.Path;
            QueueLog(L.Text("Log.Start", label, Runtime.Root, history.Started));
            ProcessResult? result = null;
            for (int i = 0; i < commands.Count; i++)
            {
                _operation.Token.ThrowIfCancellationRequested();
                QueueLog($"> {commands[i].Preview}\n");
                using var runner = new ProcessRunner(() => new JobObject()); _runner = runner;
                runner.Output += QueueLog;
                result = await runner.RunAsync(commands[i], Runtime.Root, environment, _operation.Token);
                QueueLog(L.Text("Log.Exit", result.ExitCode));
                if (result.ExitCode != 0 || result.Cancelled || result.TimedOut) break;
            }
            history.ExitCode = result?.ExitCode ?? 0;
            history.Status = result?.TimedOut == true ? L.Text("Text.136") : result?.Cancelled == true ? L.Text("Text.137") : history.ExitCode == 0 ? L.Text("Text.138") : L.Text("Text.135");
            var statusKey = result?.TimedOut == true ? "Text.136" : result?.Cancelled == true ? "Text.137" : history.ExitCode == 0 ? "Text.138" : "Text.135";
            SetStatus(() => statusKey == "Text.138" ? L.Text("Text.139") : L.Text("Status.Exit", L.Text(statusKey), history.ExitCode));
            if (history.Status == L.Text("Text.138")) Progress = 100;
        }
        catch (OperationCanceledException) { history.Status = L.Text("Text.137"); SetStatus(() => L.Text("Text.140")); }
        catch (Exception e) { QueueLog(L.Text("Text.141") + e.Message + "\n"); Notify(e); SetStatus(() => L.Text("Text.142")); }
        finally
        {
            _clock.Stop(); history.DurationSeconds = _clock.Elapsed.TotalSeconds;
            QueueLog(L.Text("Log.End", history.Status, history.DurationSeconds)); FlushLog();
            _logFile?.Dispose(); _logFile = null; _runner = null; _operation.Dispose(); _operation = null;
            IsIndeterminate = false; Busy = false;
            State.History.Add(history); SaveState(); RefreshHistory(); Raise(nameof(EnvironmentBadge)); Raise(nameof(EnvironmentAvailable));
        }
    }
    private void QueueLog(string text)
    {
        if (!_diskLogFailed)
        {
            try { _logFile?.Append(text); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _diskLogFailed = true; NotifyLocalized(() => L.Text("Text.143") + e.Message); }
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
        if (_droppedUiOutput) { batch.Append(L.Text("Text.144")); _droppedUiOutput = false; }
        if (batch.Length == 0) return;
        var value = LogText + batch;
        LogText = value.Length > 240_000 ? L.Text("Text.145") + value[^220_000..] : value;
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
    public void Stop() { _operation?.Cancel(); _runner?.Stop(); SetStatus(() => L.Text("Text.146")); }
    public void ApplyConfiguration(LauncherConfig draft)
    {
        if (!CanEdit) throw new ConfigException(new ProjectLauncher.Core.Localization.LocalizedDiagnostic("Text.147", []));
        Snapshot = ConfigStore.Save(Snapshot.Path, draft, Snapshot.Hash);
        Runtime = new(Config, Snapshot.Path); State.TrustedHash = ""; StateStore.Sanitize(State, Config); SaveState();
        _environmentVersionRender = () => L.Text("Text.115"); _environmentDetailRender = () => L.Text("Text.148");
        EnvironmentVersion = _environmentVersionRender(); EnvironmentDetail = _environmentDetailRender();
        RaiseAllConfiguration(); ConfigurationChanged?.Invoke(); NotifyLocalized(() => L.Text("Text.149"));
    }
    public void ReloadConfiguration()
    {
        if (!CanEdit) throw new ConfigException(new ProjectLauncher.Core.Localization.LocalizedDiagnostic("Text.150", []));
        Snapshot = ConfigStore.Load(Snapshot.Path); Runtime = new(Config, Snapshot.Path); StateStore.Sanitize(State, Config);
        RaiseAllConfiguration(); ConfigurationChanged?.Invoke(); NotifyLocalized(() => L.Text("Text.151"));
    }
    private void RaiseAllConfiguration()
    {
        foreach (var name in new[] { nameof(Config), nameof(ProjectName), nameof(ProjectDescription), nameof(ProjectVersion), nameof(ProjectRoot), nameof(RuntimeLabel), nameof(EnvironmentBadge), nameof(EnvironmentAvailable) }) Raise(name);
    }
    private void RefreshHistory() { History.Clear(); foreach (var item in State.History.AsEnumerable().Reverse()) History.Add(item); }
    public void Dispose() { _timer.Stop(); _operation?.Cancel(); _runner?.Stop(); _logFile?.Dispose(); }
}
