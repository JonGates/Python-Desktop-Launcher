using System.Windows;
using System.Windows.Controls;
using ProjectLauncher.Desktop.Services;
using ProjectLauncher.Desktop.ViewModels;

namespace ProjectLauncher.Desktop.Views;

public partial class EnvironmentView : UserControl
{
    private readonly ShellViewModel _shell;
    public EnvironmentView(ShellViewModel shell)
    {
        InitializeComponent(); _shell = shell; DataContext = shell;
        Loaded += (_, _) => Refresh();
    }
    public void Refresh()
    {
        PythonPathBox.Text = _shell.Runtime.PythonPath;
        UvPathBox.Text = _shell.Runtime.UvPath ?? "未找到（venv 模式可使用已安装的 Python）";
        ConfigPathBox.Text = _shell.Snapshot.Path;
    }
    private async void Initialize_Click(object sender, RoutedEventArgs e) { await _shell.InitializeAsync(Window.GetWindow(this)); Refresh(); }
    private async void Probe_Click(object sender, RoutedEventArgs e) { await _shell.ProbeAsync(Window.GetWindow(this)); Refresh(); }
    private void Terminal_Click(object sender, RoutedEventArgs e) => _shell.Navigate("terminal");
    private void Settings_Click(object sender, RoutedEventArgs e) => _shell.Navigate("settings");
    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (Dialogs.Confirm(Window.GetWindow(this), "中断当前环境操作？", "中断依赖安装可能留下不完整环境。可以稍后重新同步；本程序不会自动删除目录。", "停止", true)) _shell.Stop();
    }
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText($"Project Launcher 2.0.0-preview.1\n系统：{Environment.OSVersion}\n进程架构：{System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}\n配置：{_shell.Snapshot.Path}\n项目：{_shell.Runtime.Root}\n模式：{_shell.Config.Runtime.Mode}\n项目 Python：{_shell.Runtime.PythonPath}\nuv：{_shell.Runtime.UvPath ?? "未找到"}\n\n{_shell.EnvironmentDetail}");
            _shell.Notify("已复制环境诊断信息；未包含 runtime.env 的值或表单密码。");
        }
        catch (Exception ex) { _shell.Notify(ex.Message); }
    }
}
