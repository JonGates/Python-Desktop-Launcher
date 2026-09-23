using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using ProjectLauncher.Core;
using ProjectLauncher.Desktop.Services;
using ProjectLauncher.Desktop.ViewModels;

namespace ProjectLauncher.Desktop.Views;

public partial class RunView : UserControl, IDisposable
{
    private readonly ShellViewModel _shell;
    private Dictionary<string, object?> _values = new();
    private bool _loading;
    private ActionDefinition? _action;
    public string SelectedActionId => _action?.Id ?? "";
    public RunView(ShellViewModel shell)
    {
        _loading = true; InitializeComponent(); _shell = shell; DataContext = shell;
        _values = CommandBuilder.EffectiveValues(shell.Config, shell.State.Values);
        _action = shell.Config.Actions.FirstOrDefault(a => a.Id == shell.State.LastAction) ?? shell.Config.Actions.FirstOrDefault();
        AdvancedCheck.IsChecked = shell.State.ShowAdvanced;
        Form.ValuesChanged += RefreshPreview;
        SizeChanged += (_, _) => ParameterCard.MaxHeight = Math.Max(140, ActualHeight * 0.48);
        _shell.PropertyChanged += Shell_PropertyChanged;
        RefreshPresets(); _loading = false; ConfigureForm();
    }
    private void ConfigureForm()
    {
        var action = _action;
        EmptyPanel.Visibility = action is null ? Visibility.Visible : Visibility.Collapsed;
        ExecutionBar.Visibility = action is null ? Visibility.Collapsed : Visibility.Visible;
        var hasParameters = action is not null && CommandBuilder.SelectedParameters(_shell.Config, action).Any();
        ParameterCard.Visibility = hasParameters ? Visibility.Visible : Visibility.Collapsed;
        ParameterRow.Height = GridLength.Auto;
        if (action is null) { ActionTitle.Localize(TextBlock.TextProperty, "Ui.007"); ActionSubtitle.Localize(TextBlock.TextProperty, "Text.166"); return; }
        ActionTitle.Text = action.ToString();
        ActionSubtitle.Text = _shell.ProjectName + "  /  " + action.Id;
        Form.Configure(_shell.Config, action, _values, _shell.Runtime.Root); Form.ShowAdvanced = AdvancedCheck.IsChecked == true; RefreshPreview();
    }
    public Dictionary<string, object?> CaptureValues() => Form.GetValues();
    public void SelectAction(string id)
    {
        if (_loading || _shell.Busy || _action?.Id == id) return;
        var action = _shell.Config.Actions.FirstOrDefault(a => a.Id == id);
        if (action is null) return;
        _values = Form.GetValues(); _action = action; _shell.State.LastAction = id; ConfigureForm();
    }
    private void Advanced_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || _shell is null) return;
        Form.ShowAdvanced = AdvancedCheck.IsChecked == true; _shell.State.ShowAdvanced = Form.ShowAdvanced;
    }
    private void RefreshPreview()
    {
        if (_loading || _action is null) return;
        var action = _action;
        try
        {
            var command = CommandBuilder.Build(_shell.Config, action.Id, Form.GetValues(), _shell.Runtime.Root, _shell.Runtime.PythonPath);
            PreviewBox.Text = command.Preview;
            PreviewHint.Dynamic(TextBlock.TextProperty, () => _shell.Runtime.Exists ? L.Text("Text.167") : L.Text("Text.168"));
        }
        catch (Exception e) { PreviewBox.Localize(TextBox.TextProperty, "Text.169"); PreviewHint.Dynamic(TextBlock.TextProperty, () => e.Message); }
    }
    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_action is null) return;
        await _shell.RunActionAsync(Window.GetWindow(this), _action.Id, Form.GetValues());
    }
    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (Dialogs.Confirm(Window.GetWindow(this), L.Text("Text.015"), L.Text("Text.016"), L.Text("Text.017"), true)) _shell.Stop();
    }
    private void Reset_Click(object sender, RoutedEventArgs e) { Form.ApplyValues(new()); PresetBox.SelectedIndex = 0; }
    private void Settings_Click(object sender, RoutedEventArgs e) => _shell.Navigate("settings");
    private void Environment_Click(object sender, RoutedEventArgs e) => _shell.Navigate("environment");
    private void OpenOutput_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string path = Path.GetFullPath(string.IsNullOrWhiteSpace(_shell.Config.App.OutputDir) ? "." : _shell.Config.App.OutputDir, _shell.Runtime.Root);
            // A dynamic output parameter does not implicitly replace app.output_dir.
            if (!Directory.Exists(path)) { _shell.Notify(L.Text("Text.170") + path); return; }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { _shell.Notify(ex); }
    }
    private void CopyCommand_Click(object sender, RoutedEventArgs e) { try { Clipboard.SetText(PreviewBox.Text); _shell.Notify(L.Text("Text.171")); } catch (Exception ex) { _shell.Notify(ex); } }
    private void ClearLog_Click(object sender, RoutedEventArgs e) => _shell.ClearLog();
    private void ExportLog_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { FileName = "launcher-visible-log.txt", Filter = L.Text("Text.172") };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) try { File.WriteAllText(dialog.FileName, _shell.LogText, new System.Text.UTF8Encoding(false)); } catch (Exception ex) { _shell.Notify(ex); }
    }
    private void RefreshPresets()
    {
        PresetBox.ItemsSource = new[] { "\u0001current", "\u0001defaults" }.Concat(_shell.State.Presets.Keys.Order()).ToList();
        L.Options(PresetBox, new Dictionary<string, string> { ["\u0001current"] = "Text.173", ["\u0001defaults"] = "Text.174" });
        PresetBox.SelectedIndex = 0;
    }
    private void Preset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || PresetBox.SelectedItem is not string name) return;
        if (name == "\u0001defaults") Form.ApplyValues(new());
        else if (_shell.State.Presets.TryGetValue(name, out var values)) Form.ApplyValues(values);
    }
    private void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        var name = Dialogs.Input(Window.GetWindow(this), L.Text("Ui.056"), L.Text("Text.176"), L.Text("Text.177"));
        if (name is null) return;
        if (name.Length > 64 || name.Any(char.IsControl) || name == L.Text("Text.173") || name == L.Text("Text.174")) { _shell.Notify(L.Text("Text.178")); return; }
        if (_shell.State.Presets.ContainsKey(name) && !Dialogs.Confirm(Window.GetWindow(this), L.Text("Text.179"), name, L.Text("Text.180"))) return;
        _shell.State.Presets[name] = Form.GetValues(); _shell.SaveState(); _loading = true; RefreshPresets(); PresetBox.SelectedItem = name; _loading = false; _shell.Notify(L.Text("Text.181"));
    }
    private void DeletePreset_Click(object sender, RoutedEventArgs e)
    {
        if (PresetBox.SelectedItem is not string name || !_shell.State.Presets.ContainsKey(name)) return;
        if (!Dialogs.Confirm(Window.GetWindow(this), L.Text("Text.182"), name, L.Text("Text.032"), true)) return;
        _shell.State.Presets.Remove(name); _shell.SaveState(); _loading = true; RefreshPresets(); _loading = false;
    }
    private void History_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (HistoryList.SelectedItem is RunHistory record) Dialogs.Info(Window.GetWindow(this), L.Text("Text.183"), L.Text("History.Detail", record.ActionLabel, record.Started, record.Status, record.ExitCode, record.DurationSeconds, record.Command, record.LogPath));
    }
    private void Shell_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.LogText) && AutoScrollCheck.IsChecked == true) Dispatcher.InvokeAsync(() => LogBox.ScrollToEnd());
        if (e.PropertyName == nameof(ShellViewModel.EnvironmentBadge)) RefreshPreview();
    }
    public void Dispose() => _shell.PropertyChanged -= Shell_PropertyChanged;
}
