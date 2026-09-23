using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;
using ProjectLauncher.Core;
using ProjectLauncher.Desktop.Controls;
using ProjectLauncher.Desktop.Services;
using ProjectLauncher.Desktop.ViewModels;

namespace ProjectLauncher.Desktop.Views;

public partial class SettingsView : UserControl
{
    private readonly ShellViewModel _shell;
    private LauncherConfig _draft = new();
    private ActionWorkspace? _workspace;
    private bool _loading = true;
    public bool HasUnsavedChanges { get; private set; }
    public SettingsView(ShellViewModel shell)
    {
        InitializeComponent(); _shell = shell; DataContext = shell;
        ModeBox.ItemsSource = new[] { "venv", "uv", "existing" };
        ShellBox.ItemsSource = new[] { "auto", "powershell", "pwsh", "cmd" };
        AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, e) => { if (!FromWorkspace(e.OriginalSource)) MarkDirty(); }));
        AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler((_, e) => { if (e.OriginalSource is ComboBox && !FromWorkspace(e.OriginalSource)) MarkDirty(); }));
        LoadDraft(shell.Config);
    }
    private bool FromWorkspace(object source) => source is DependencyObject d && _workspace is not null && (_workspace == d || _workspace.IsAncestorOf(d));
    public void ReloadFromShell() => LoadDraft(_shell.Config);
    private void MarkDirty()
    {
        if (_loading) return;
        HasUnsavedChanges = true; DirtyLabel.Localize(TextBlock.TextProperty, "Text.184");
    }
    private void LoadDraft(LauncherConfig config)
    {
        _loading = true; _draft = ConfigStore.Clone(config);
        NameBox.Text = _draft.App.Name; DescriptionBox.Text = _draft.App.Description; VersionBox.Text = _draft.App.Version; OutputBox.Text = _draft.App.OutputDir;
        ModeBox.SelectedItem = _draft.Runtime.Mode; ShellBox.SelectedItem = _draft.Runtime.Shell;
        ProjectDirBox.Text = _draft.Runtime.ProjectDir; VenvBox.Text = _draft.Runtime.Venv;
        PythonBox.Text = _draft.Runtime.Python; RequirementsBox.Text = _draft.Runtime.Requirements;
        EnvBox.Text = JsonSerializer.Serialize(_draft.Runtime.Env, new JsonSerializerOptions { WriteIndented = true });
        _workspace = new ActionWorkspace(_draft, Path.GetDirectoryName(_shell.Snapshot.Path)!, MarkDirty); ActionWorkspaceHost.Content = _workspace;
        _workspace.ValidationNotice += message => {
            if (message is null) _shell.ClearNotification("action-editor");
            else _shell.NotifyLocalized(() => message.Message, "action-editor");
        };
        YamlBox.Text = ConfigStore.Serialize(_draft);
        HasUnsavedChanges = false; DirtyLabel.Localize(TextBlock.TextProperty, "Ui.097"); _loading = false;
    }
    private void CommitEditors()
    {
        Dictionary<string, string> env;
        try { env = string.IsNullOrWhiteSpace(EnvBox.Text) ? new() : JsonSerializer.Deserialize<Dictionary<string, string>>(EnvBox.Text) ?? new(); }
        catch (JsonException e) { EnvBox.Focus(); throw new ConfigException(L.Text("Text.186") + e.Message); }
        _draft.App.Name = NameBox.Text.Trim(); _draft.App.Description = DescriptionBox.Text; _draft.App.Version = VersionBox.Text.Trim(); _draft.App.OutputDir = OutputBox.Text.Trim();
        _draft.Runtime.Mode = ModeBox.SelectedItem?.ToString() ?? "venv"; _draft.Runtime.Shell = ShellBox.SelectedItem?.ToString() ?? "auto";
        _draft.Runtime.ProjectDir = ProjectDirBox.Text.Trim(); _draft.Runtime.Venv = VenvBox.Text.Trim();
        _draft.Runtime.Python = PythonBox.Text.Trim(); _draft.Runtime.Requirements = RequirementsBox.Text.Trim(); _draft.Runtime.Env = env;
        try { _workspace?.Commit(); }
        catch (ConfigException ex) { _shell.NotifyLocalized(() => ex.Message, "action-editor"); throw; }
    }
    private void GenerateYaml_Click(object sender, RoutedEventArgs e) => TryEdit(() => { CommitEditors(); YamlBox.Text = ConfigStore.Serialize(_draft); MarkDirty(); });
    private void ApplyYaml_Click(object sender, RoutedEventArgs e) => TryEdit(() =>
    {
        var parsed = ConfigStore.Parse(YamlBox.Text);
        if (!Dialogs.Confirm(Window.GetWindow(this), L.Text("Text.187"), L.Text("Text.188"), L.Text("Text.189"))) return;
        LoadDraft(parsed); MarkDirty();
    });
    private void Save_Click(object sender, RoutedEventArgs e) => TryEdit(() =>
    {
        LauncherConfig config;
        if (SettingTabs.SelectedIndex == 2) config = ConfigStore.Parse(YamlBox.Text);
        else { CommitEditors(); config = _draft; }
        if (Window.GetWindow(this) is MainWindow main) main.ApplyAndView(config, _workspace?.SelectedActionId ?? "");
        else _shell.ApplyConfiguration(config);
    });
    private void Reload_Click(object sender, RoutedEventArgs e) => TryEdit(() =>
    {
        if (HasUnsavedChanges && !Dialogs.Confirm(Window.GetWindow(this), L.Text("Text.190"), L.Text("Text.191"), L.Text("Ui.095"), true)) return;
        _shell.ReloadConfiguration();
    });
    private void BrowseProject_Click(object sender, RoutedEventArgs e)
    { var d = new OpenFolderDialog { Title = L.Text("Text.193") }; if (d.ShowDialog(Window.GetWindow(this)) == true) ProjectDirBox.Text = d.FolderName; }
    private void BrowseVenv_Click(object sender, RoutedEventArgs e)
    { var d = new OpenFolderDialog { Title = L.Text("Text.194") }; if (d.ShowDialog(Window.GetWindow(this)) == true) VenvBox.Text = d.FolderName; }
    private void TryEdit(Action action) { try { action(); } catch (Exception e) { DirtyLabel.Localize(TextBlock.TextProperty, "Text.195"); if (_shell.NotificationDetail != e.Message || !_shell.HasNotification) _shell.NotifyLocalized(() => e.Message, "settings"); } }
    public void DiscardDirtyMarker() => HasUnsavedChanges = false;
    public void SelectDesigner() => SettingTabs.SelectedIndex = 1;
    public void SelectAction(string id) { SettingTabs.SelectedIndex = 1; if (!HasUnsavedChanges) _workspace?.SelectAction(id); }
    public void SelectYaml() => SettingTabs.SelectedIndex = 2;
}
