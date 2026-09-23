using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using ProjectLauncher.Core;
using ProjectLauncher.Desktop.Services;
using ProjectLauncher.Desktop.ViewModels;
using ProjectLauncher.Desktop.Views;

namespace ProjectLauncher.Desktop;

public partial class MainWindow : Window
{
    public ShellViewModel Shell { get; }
    private RunView _run = null!;
    private TerminalView _terminal = null!;
    private EnvironmentView _environment = null!;
    private SettingsView _settings = null!;
    private string _page = "run";
    private bool _ready, _closing, _allowClose;
    internal SettingsView SettingsPage => _settings;
    internal TerminalView TerminalPage => _terminal;

    public MainWindow(ConfigSnapshot snapshot)
    {
        Shell = new(snapshot); ThemeService.Apply(Shell.State.Theme);
        InitializeComponent(); DataContext = Shell;
        Title = Shell.ProjectName + " · Project Launcher";
        Width = Math.Max(MinWidth, Math.Min(Shell.State.WindowWidth, SystemParameters.WorkArea.Width - 28));
        Height = Math.Max(MinHeight, Math.Min(Shell.State.WindowHeight, SystemParameters.WorkArea.Height - 28));
        CreateViews();
        Shell.NavigationRequested += ShowPage;
        Shell.ConfigurationChanged += ConfigurationChanged;
        _ready = true; ShowPage("run");
    }
    private void CreateViews()
    {
        _run = new RunView(Shell); _terminal = new TerminalView(Shell);
        _environment = new EnvironmentView(Shell); _settings = new SettingsView(Shell);
    }
    private async void ConfigurationChanged()
    {
        try
        {
            Shell.State.Values = _run.CaptureValues(); Shell.SaveState();
            _run.Dispose(); await _terminal.DisposeAsync();
            CreateViews(); Title = Shell.ProjectName + " · Project Launcher"; ShowPage(_page);
        }
        catch (Exception e) { Shell.Notify(e.Message); }
    }
    public void ShowPage(string page)
    {
        if (!_ready) return;
        _page = page;
        PageHost.Content = page switch { "terminal" => _terminal, "environment" => _environment, "settings" => _settings, _ => _run };
        switch (page)
        {
            case "terminal": TerminalNav.IsChecked = true; break;
            case "environment": EnvironmentNav.IsChecked = true; break;
            case "settings": SettingsNav.IsChecked = true; break;
            default: RunNav.IsChecked = true; break;
        }
    }
    private void Nav_Checked(object sender, RoutedEventArgs e) { if (_ready && sender is RadioButton { Tag: string page }) ShowPage(page); }
    private void Theme_Click(object sender, RoutedEventArgs e)
    { Shell.State.Theme = Shell.State.Theme == "dark" ? "light" : "dark"; ThemeService.Apply(Shell.State.Theme); Shell.SaveState(); }
    private void Minimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void Maximize_Click(object sender, RoutedEventArgs e) { if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this); }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    { try { Process.Start(new ProcessStartInfo(Shell.ProjectRoot) { UseShellExecute = true }); } catch (Exception ex) { Shell.Notify(ex.Message); } }
    private void DismissNotice_Click(object sender, RoutedEventArgs e) => Shell.DismissNotification();
    private void NoticeDetail_Click(object sender, RoutedEventArgs e) => Dialogs.Info(this, "详细信息", Shell.NotificationDetail);
    private void About_Click(object sender, RoutedEventArgs e) => Dialogs.Info(this, "Project Launcher 2.0 · C# Preview",
        "原生 WPF 桌面启动器 · .NET 10\n\n一个项目，一份 launcher.yaml。界面与 Python 业务环境分离。\n\n设置：可视化参数 / 启动 argv / 完整 YAML。\n终端：ConPTY + 原生基础 VT 渲染，不依赖浏览器。\n\n快捷键\nCtrl + ,    项目设置（终端内不拦截）\nCtrl + Shift + 1 / 2 / 3 / 4    切换四个页面（终端内不拦截）\n终端 Ctrl + Shift + C / V    复制 / 粘贴\n终端 Ctrl + 鼠标滚轮    字号\n\n内置终端不承诺兼容所有全屏 TUI、复杂 emoji 和鼠标协议；可使用「系统终端」作为替代。\n\n许可证：MIT。第三方组件遵循各自许可证。\n源码预览版：请先完成 Windows 构建及验收，再用于正式业务。" );
    private void SwitchProject_Click(object sender, RoutedEventArgs e)
    {
        if (!Shell.CanEdit) { Shell.Notify("请先结束运行任务并关闭项目终端，再切换项目。"); return; }
        if (_settings.HasUnsavedChanges && !Dialogs.Confirm(this, "有未保存的设置", "切换项目会丢弃当前未保存的设置草稿。是否继续？", "继续切换")) return;
        var dialog = new OpenFileDialog { Title = "选择目标项目的 launcher.yaml", Filter = "启动配置 (*.yaml;*.yml)|*.yaml;*.yml", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var snapshot = ConfigStore.Load(dialog.FileName);
            if (ProjectEnvironment.SamePath(snapshot.Path, Shell.Snapshot.Path)) { Shell.Notify("当前已经打开这份项目配置。"); return; }
            App.CurrentApp.OpenProject(snapshot);
            _settings.DiscardDirtyMarker(); Close();
        }
        catch (Exception ex) { Shell.Notify(ex.Message); }
    }
    private void Deploy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // A framework-dependent apphost cannot be copied on its own. Deployment is explicitly limited to published single-file builds.
#pragma warning disable IL3000
            if (Assembly.GetExecutingAssembly().Location.Length != 0)
#pragma warning restore IL3000
                throw new ConfigException("当前运行的是开发 / 非单文件版本，不能只复制一个 EXE。请先运行源码包的 Build.cmd，使用 artifacts\\portable\\Launcher.exe 再点击「接入其他项目」。");
            var folder = new OpenFolderDialog { Title = "选择要接入的现有 Python 项目", Multiselect = false };
            if (folder.ShowDialog(this) != true) return;
            var entry = Dialogs.Input(this, "确认业务入口", "只用于目标目录没有 launcher.yaml 时生成初始配置。不会改写已有配置和业务代码。", "main.py");
            if (entry is null) return;
            if (!Dialogs.Confirm(this, "把启动器接入这个项目？", folder.FolderName + "\n\n只复制 Launcher.exe。缺少 launcher.yaml 时创建最小配置；已有配置原样保留。\n\n不会复制 .venv、用户状态、Demo 或业务代码。同名 EXE 存在时拒绝覆盖。", "接入项目")) return;
            var files = ProjectInstaller.Install(Environment.ProcessPath ?? throw new ConfigException("无法定位当前 EXE。"), folder.FolderName, entry);
            Dialogs.Info(this, "已添加启动器文件", string.Join("\n", files) + "\n\n进入目标目录双击 Launcher.exe，再到「项目设置」核对入口、环境和参数。原项目不需要改写。");
        }
        catch (Exception ex) { Shell.Notify(ex.Message); }
    }
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_page == "terminal") return; // Do not steal shell / editor shortcuts.
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.OemComma) { ShowPage("settings"); e.Handled = true; }
        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            string? target = e.Key switch { Key.D1 => "run", Key.D2 => "terminal", Key.D3 => "environment", Key.D4 => "settings", _ => null };
            if (target is not null) { ShowPage(target); e.Handled = true; }
        }
    }
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true; if (_closing) return;
        if (_settings.HasUnsavedChanges && !Dialogs.Confirm(this, "有未保存的项目设置", "关闭窗口会丢弃设置页的未保存草稿。", "放弃草稿并关闭")) return;
        if ((Shell.Busy || Shell.OpenTerminalCount > 0) && !Dialogs.Confirm(this, "停止运行并关闭？", "仍有任务或项目终端运行中。关闭启动器将终止由本窗口管理的进程，未完成输出可能不完整。", "停止并关闭", true)) return;
        _closing = true; IsEnabled = false;
        try
        {
            Shell.State.Values = _run.CaptureValues();
            if (WindowState == WindowState.Normal) { Shell.State.WindowWidth = Width; Shell.State.WindowHeight = Height; }
            Shell.Stop();
            await _terminal.DisposeAsync();
            var until = DateTime.UtcNow.AddSeconds(15);
            while (Shell.Busy && DateTime.UtcNow < until) await Task.Delay(100);
            Shell.SaveState();
        }
        catch (Exception ex) { App.WriteCrash(ex); }
        finally
        {
            _run.Dispose(); Shell.Dispose(); Shell.NavigationRequested -= ShowPage; Shell.ConfigurationChanged -= ConfigurationChanged;
            _allowClose = true; App.CurrentApp.ReleaseProject(this); Close();
        }
    }
}
