using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ProjectLauncher.Core;
using ProjectLauncher.Desktop.Controls;
using ProjectLauncher.Desktop.Services;
using ProjectLauncher.Desktop.ViewModels;
using ProjectLauncher.Windows;

namespace ProjectLauncher.Desktop.Views;

public sealed class TerminalTab : ObservableObject
{
    public string Title { get; set; } = "Terminal";
    private string _status = "启动中";
    public string Status { get => _status; set => Set(ref _status, value); }
    public TerminalControl Control { get; } = new();
    public ConPtySession Session { get; } = new();
    public ConcurrentQueue<string> Output { get; } = new();
    public int PendingCharacters;
    public volatile bool Overflow;
    public bool Starting { get; set; } = true;
}

public partial class TerminalView : UserControl, IAsyncDisposable
{
    private readonly ShellViewModel _shell;
    public ObservableCollection<TerminalTab> Tabs { get; } = [];
    private readonly List<(Process Process, JobObject Job)> _external = [];
    private readonly DispatcherTimer _timer;
    private bool _disposed;
    public TerminalView(ShellViewModel shell)
    {
        InitializeComponent(); _shell = shell; DataContext = shell; TermTabs.ItemsSource = Tabs;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += (_, _) => Drain(); _timer.Start();
    }
    private async Task NewSessionAsync(string choice)
    {
        if (_shell.Busy || _disposed) return;
        if (Tabs.Count >= 4) { _shell.Notify("当前最多保留 4 个内置终端标签。请先关闭不需要的会话。"); return; }
        try
        {
            if (!_shell.ConfirmTrust(Window.GetWindow(this))) return;
            var command = _shell.Runtime.ShellCommand(choice);
            var environment = _shell.Runtime.ExecutionEnvironment();
            var tab = new TerminalTab { Title = choice == "python" ? "Python REPL" : choice == "cmd" ? "CMD" : "PowerShell" };
            tab.Control.PathQuoter = command[0].Contains("powershell", StringComparison.OrdinalIgnoreCase) || command[0].Contains("pwsh", StringComparison.OrdinalIgnoreCase)
                ? path => "'" + path.Replace("'", "''") + "'" : WindowsArguments.Quote;
            tab.Control.Input += tab.Session.Write; tab.Control.Error += _shell.Notify;
            tab.Session.Output += text =>
            {
                if (Interlocked.Add(ref tab.PendingCharacters, text.Length) > 4_000_000) { Interlocked.Add(ref tab.PendingCharacters, -text.Length); tab.Overflow = true; return; }
                tab.Output.Enqueue(text);
            };
            tab.Session.Diagnostic += _shell.Notify;
            tab.Session.Exited += code => Dispatcher.InvokeAsync(() => { tab.Status = "已退出 " + code; UpdateCount(); });
            tab.Control.TerminalResized += (cols, rows) => _ = Task.Run(() => { try { tab.Session.Resize(cols, rows); } catch (Exception e) { _shell.Notify(e.Message); } });
            Tabs.Add(tab); TermTabs.SelectedItem = tab; EmptyState.Visibility = Visibility.Collapsed; UpdateCount();
            try
            {
                await Task.Run(() => tab.Session.Start(command, _shell.Runtime.Root, environment, tab.Control.Screen.Columns, tab.Control.Screen.Rows));
                tab.Starting = false; tab.Status = tab.Session.IsRunning ? "运行中" : "已退出"; UpdateCount();
                _ = Dispatcher.InvokeAsync(() => tab.Control.Focus());
            }
            catch { tab.Starting = false; await tab.Session.CloseAsync(); Tabs.Remove(tab); UpdateCount(); throw; }
        }
        catch (Exception e) { _shell.Notify("项目终端启动失败：" + e.Message); }
    }
    private void Drain()
    {
        foreach (var tab in Tabs.ToList())
        {
            int size = 0; var buffer = new System.Text.StringBuilder();
            while (size < 120_000 && tab.Output.TryDequeue(out var text)) { size += text.Length; Interlocked.Add(ref tab.PendingCharacters, -text.Length); buffer.Append(text); }
            if (tab.Overflow)
            {
                tab.Control.ResetScreen(); tab.Overflow = false;
                _shell.Notify("终端输出超过显示缓冲区，屏幕已重置以控制内存。大量日志任务建议使用「运行项目」而不是交互终端。");
            }
            if (buffer.Length > 0) tab.Control.Feed(buffer.ToString());
        }
        foreach (var item in _external.ToList())
        {
            try { if (!item.Process.HasExited) continue; } catch (InvalidOperationException) { }
            item.Job.Dispose(); item.Process.Dispose(); _external.Remove(item);
        }
        UpdateCount();
    }
    private void UpdateCount()
    {
        _shell.OpenTerminalCount = Tabs.Count(t => t.Starting || t.Session.IsRunning) + _external.Count;
        EmptyState.Visibility = Tabs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private async void NewPowerShell_Click(object sender, RoutedEventArgs e) => await NewSessionAsync("auto");
    private async void NewCmd_Click(object sender, RoutedEventArgs e) => await NewSessionAsync("cmd");
    private async void NewPython_Click(object sender, RoutedEventArgs e) => await NewSessionAsync("python");
    private async void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not TerminalTab tab) return;
        if (tab.Starting) { _shell.Notify("终端仍在启动，请稍后关闭。"); return; }
        if (tab.Session.IsRunning && !Dialogs.Confirm(Window.GetWindow(this), "关闭这个终端？", "此操作会终止该会话及其归属进程，不仅仅是隐藏标签。", "关闭终端", true)) return;
        try { await tab.Session.CloseAsync().WaitAsync(TimeSpan.FromSeconds(15)); Tabs.Remove(tab); UpdateCount(); }
        catch (Exception ex) { _shell.Notify(ex.Message); }
    }
    private TerminalTab? Selected => TermTabs.SelectedItem as TerminalTab;
    private void Copy_Click(object sender, RoutedEventArgs e) => Selected?.Control.Copy();
    private void Paste_Click(object sender, RoutedEventArgs e) => Selected?.Control.Paste();
    private void Interrupt_Click(object sender, RoutedEventArgs e) { try { Selected?.Session.Write("\u0003"); } catch (Exception ex) { _shell.Notify(ex.Message); } }
    private void InputLine_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { SendLine(); e.Handled = true; } }
    private void SendLine_Click(object sender, RoutedEventArgs e) => SendLine();
    private void SendLine()
    {
        try
        {
            if (Selected?.Session.IsRunning != true) { _shell.Notify("请先新建或选择一个运行中的终端会话。"); return; }
            Selected.Session.Write(InputLineBox.Text + "\r"); InputLineBox.Clear();
        }
        catch (Exception e) { _shell.Notify(e.Message); }
    }
    private void External_Click(object sender, RoutedEventArgs e)
    {
        if (_shell.Busy) return;
        try
        {
            if (!_shell.ConfirmTrust(Window.GetWindow(this))) return;
            var args = _shell.Runtime.ShellCommand(); var env = _shell.Runtime.ExecutionEnvironment();
            var start = new ProcessStartInfo { FileName = args[0], WorkingDirectory = _shell.Runtime.Root, UseShellExecute = false, CreateNoWindow = false };
            foreach (var argument in args.Skip(1)) start.ArgumentList.Add(argument);
            start.Environment.Clear(); foreach (var (key, value) in env) start.Environment[key] = value;
            var job = new JobObject(); Process? process = null;
            try
            {
                process = Process.Start(start) ?? throw new ConfigException("未能打开系统终端。"); job.Attach(process); _external.Add((process, job));
            }
            catch { if (process is not null) { try { process.Kill(true); } catch { } process.Dispose(); } job.Dispose(); throw; }
            UpdateCount(); _shell.Notify("系统终端已在项目环境中打开。它由本壳管理；关闭后才能保存环境设置。");
        }
        catch (Exception ex) { _shell.Notify(ex.Message); }
    }
    private async void CloseAll_Click(object sender, RoutedEventArgs e)
    {
        if (Tabs.Any(t => t.Starting)) { _shell.Notify("请等待终端启动完成。"); return; }
        if (!Dialogs.Confirm(Window.GetWindow(this), "关闭全部项目终端？", "包括内置标签和通过本壳打开的系统终端；会终止它们的归属进程。", "关闭全部", true)) return;
        try { await CloseAllAsync(); } catch (Exception ex) { _shell.Notify(ex.Message); }
    }
    public async Task CloseAllAsync()
    {
        foreach (var tab in Tabs.ToList()) await tab.Session.CloseAsync().WaitAsync(TimeSpan.FromSeconds(15));
        Tabs.Clear();
        foreach (var item in _external.ToList())
        {
            item.Job.Dispose();
            try { if (!item.Process.HasExited) item.Process.Kill(true); await item.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
            item.Process.Dispose(); _external.Remove(item);
        }
        UpdateCount();
    }
    public void AddPreviewTab()
    {
        // Called only by --ui-smoke-test; never represents a real running process.
        var tab = new TerminalTab { Title = "UI smoke-test", Status = "渲染检查", Starting = false };
        tab.Control.Feed("\u001b[38;2;161;154;255mProject Launcher · terminal rendering smoke test\u001b[0m\r\n\r\n项目环境：.venv\r\nPS D:\\projects\\demo> python --version\r\nPython version comes from your project.\r\n\u001b[32mUTF-8 / 中文 / ANSI colors\u001b[0m\r\n\r\nThis is UI test data, not executed commands.\r\n");
        Tabs.Add(tab); TermTabs.SelectedItem = tab; UpdateCount();
    }
    public async ValueTask DisposeAsync() { if (_disposed) return; _disposed = true; try { await CloseAllAsync(); } finally { _timer.Stop(); } }
}
