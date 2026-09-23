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
        UvPathBox.Text = _shell.Runtime.UvPath ?? L.Text("Text.152");
        ConfigPathBox.Text = _shell.Snapshot.Path;
    }
    private async void Initialize_Click(object sender, RoutedEventArgs e) { await _shell.InitializeAsync(Window.GetWindow(this)); Refresh(); }
    private async void Probe_Click(object sender, RoutedEventArgs e) { await _shell.ProbeAsync(Window.GetWindow(this)); Refresh(); }
    private void Terminal_Click(object sender, RoutedEventArgs e) => _shell.Navigate("terminal");
    private void Settings_Click(object sender, RoutedEventArgs e) => _shell.Navigate("settings");
    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (Dialogs.Confirm(Window.GetWindow(this), L.Text("Text.153"), L.Text("Text.154"), L.Text("Ui.052"), true)) _shell.Stop();
    }
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(L.Text("Environment.Diagnostics", Environment.OSVersion, System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture, _shell.Snapshot.Path, _shell.Runtime.Root, _shell.Config.Runtime.Mode, _shell.Runtime.PythonPath, _shell.Runtime.UvPath ?? L.Text("Text.156"), _shell.EnvironmentDetail));
            _shell.NotifyLocalized(() => L.Text("Text.157"));
        }
        catch (Exception ex) { _shell.Notify(ex); }
    }
}
