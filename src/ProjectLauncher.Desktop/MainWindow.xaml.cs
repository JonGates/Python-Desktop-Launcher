using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ProjectLauncher.Core;
using ProjectLauncher.Desktop.Services;
using ProjectLauncher.Desktop.ViewModels;
using ProjectLauncher.Desktop.Views;

namespace ProjectLauncher.Desktop;

public partial class MainWindow : Window
{
    public ShellViewModel Shell { get; }
    private void OfficialSite_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        e.Handled = true;
        try { Process.Start(new ProcessStartInfo("https://github.com/JonGates/Python-Desktop-Launcher") { UseShellExecute = true }); }
        catch (Exception ex) { Shell.NotifyLocalized(() => L.Text("Text.014") + ex.Message); }
    }
    private RunView _run = null!;
    private TerminalView _terminal = null!;
    private EnvironmentView _environment = null!;
    private SettingsView _settings = null!;
    private string _page = "run";
    private string? _pendingAction;
    private bool _ready, _closing, _allowClose, _syncActions;
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
        if (LocalizationService.Current.PreferenceError is string error) Shell.Notify(error);
    }
    private void CreateViews()
    {
        _run = new RunView(Shell); _terminal = new TerminalView(Shell);
        _environment = new EnvironmentView(Shell); _settings = new SettingsView(Shell);
        _syncActions = true;
        try { ActionList.ItemsSource = Shell.Config.Actions; ActionList.SelectedItem = Shell.Config.Actions.FirstOrDefault(a => a.Id == _run.SelectedActionId); }
        finally { _syncActions = false; }
    }
    public void ApplyAndView(LauncherConfig config, string actionId)
    {
        _pendingAction = actionId;
        try { Shell.ApplyConfiguration(config); }
        catch { _pendingAction = null; throw; }
    }
    private async void ConfigurationChanged()
    {
        try
        {
            Shell.State.Values = _run.CaptureValues(); Shell.SaveState();
            _run.Dispose(); await _terminal.DisposeAsync();
            CreateViews(); Title = Shell.ProjectName + " · Project Launcher";
            if (_pendingAction is { } id) { _pendingAction = null; _run.SelectAction(id); ShowPage("run"); }
            else ShowPage(_page);
        }
        catch (Exception e) { Shell.Notify(e.Message); }
    }
    public void ShowPage(string page)
    {
        if (!_ready) return;
        _page = page;
        if (page == "settings") _settings.SelectAction(_run.SelectedActionId);
        PageHost.Content = page switch { "terminal" => _terminal, "environment" => _environment, "settings" => _settings, _ => _run };
        _syncActions = true;
        try
        {
            TerminalNav.IsChecked = page == "terminal"; EnvironmentNav.IsChecked = page == "environment"; SettingsNav.IsChecked = page == "settings";
            RunNav.Tag = page == "run" ? "active" : "";
            ActionList.SelectedItem = page == "run" ? Shell.Config.Actions.FirstOrDefault(a => a.Id == _run.SelectedActionId) : null;
            if (page == "run") RunNav.IsChecked = true;
        }
        finally { _syncActions = false; }
    }
    private void Nav_Checked(object sender, RoutedEventArgs e) { if (_ready && !_syncActions && sender is RadioButton { Tag: string page }) ShowPage(page); }
    private void RunGroup_Click(object sender, RoutedEventArgs e) { if (_ready && RunNav.IsChecked == true) ShowPage("run"); }
    private void Action_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _syncActions || ActionList.SelectedItem is not ActionDefinition action) return;
        if (Shell.Busy && action.Id != Shell.ActiveActionId)
        {
            _syncActions = true;
            try { ActionList.SelectedItem = _page == "run" ? Shell.Config.Actions.FirstOrDefault(a => a.Id == _run.SelectedActionId) : null; }
            finally { _syncActions = false; }
            return;
        }
        _run.SelectAction(action.Id); ShowPage("run");
    }
    private async void ActionQuick_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { DataContext: ActionDefinition action }) return;
        if (Shell.Busy)
        {
            if (Shell.ActiveActionId != action.Id) return;
            if (Dialogs.Confirm(this, L.Text("Text.015"), L.Text("Text.016"), L.Text("Text.017"), true)
                && Shell.Busy && Shell.ActiveActionId == action.Id) Shell.Stop();
            return;
        }
        _run.SelectAction(action.Id); ShowPage("run");
        await Shell.RunActionAsync(this, action.Id, _run.CaptureValues());
    }
    private void Theme_Click(object sender, RoutedEventArgs e)
    { Shell.State.Theme = Shell.State.Theme == "dark" ? "light" : "dark"; ThemeService.Apply(Shell.State.Theme); Shell.SaveState(); }
    private void Minimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void Maximize_Click(object sender, RoutedEventArgs e) { if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this); }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    { try { Process.Start(new ProcessStartInfo(Shell.ProjectRoot) { UseShellExecute = true }); } catch (Exception ex) { Shell.Notify(ex); } }
    private void DismissNotice_Click(object sender, RoutedEventArgs e) => Shell.DismissNotification();
    private void NoticeDetail_Click(object sender, RoutedEventArgs e) => Dialogs.Info(this, L.Text("Text.018"), Shell.NotificationDetail);
    private void About_Click(object sender, RoutedEventArgs e) => Dialogs.Info(this, "Project Launcher 2.0 · C# Preview",
        L.Text("Text.019") );
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
        if (_settings.HasUnsavedChanges && !Dialogs.Confirm(this, L.Text("Text.020"), L.Text("Text.021"), L.Text("Text.022"))) return;
        if ((Shell.Busy || Shell.OpenTerminalCount > 0) && !Dialogs.Confirm(this, L.Text("Text.023"), L.Text("Text.024"), L.Text("Text.025"), true)) return;
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
            _allowClose = true; App.CurrentApp.ReleaseProject(this);
            // With no running terminal, every await may finish synchronously. Let the
            // original Closing event return before closing again, or WPF rejects reentry.
            _ = Dispatcher.InvokeAsync(Close);
        }
    }
}
