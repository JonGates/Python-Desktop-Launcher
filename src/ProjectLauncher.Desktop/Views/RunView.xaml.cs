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
        if (action is null) { ActionTitle.Text = "运行项目"; ActionSubtitle.Text = "先添加一个启动动作"; return; }
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
            PreviewHint.Text = _shell.Runtime.Exists ? "参数通过独立 argv 传入，不拼接到 Shell。" : "项目环境尚未初始化；当前仅预览，不能执行。";
        }
        catch (Exception e) { PreviewBox.Text = "参数尚未完整"; PreviewHint.Text = e.Message; }
    }
    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_action is null) return;
        await _shell.RunActionAsync(Window.GetWindow(this), _action.Id, Form.GetValues());
    }
    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (Dialogs.Confirm(Window.GetWindow(this), "停止当前任务？", "这会终止任务及其归属进程，不是暂停。正在写入的文件可能不完整。", "停止任务", true)) _shell.Stop();
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
            if (!Directory.Exists(path)) { _shell.Notify("输出目录尚不存在。请先运行任务或在项目设置中核对 app.output_dir：" + path); return; }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { _shell.Notify(ex.Message); }
    }
    private void CopyCommand_Click(object sender, RoutedEventArgs e) { try { Clipboard.SetText(PreviewBox.Text); _shell.Notify("已复制脱敏命令预览。预览用于诊断，不保证可直接粘贴到所有 Shell 执行。"); } catch (Exception ex) { _shell.Notify(ex.Message); } }
    private void ClearLog_Click(object sender, RoutedEventArgs e) => _shell.ClearLog();
    private void ExportLog_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { FileName = "launcher-visible-log.txt", Filter = "文本文件|*.txt" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) try { File.WriteAllText(dialog.FileName, _shell.LogText, new System.Text.UTF8Encoding(false)); } catch (Exception ex) { _shell.Notify(ex.Message); }
    }
    private void RefreshPresets()
    {
        PresetBox.ItemsSource = new[] { "当前参数", "默认参数" }.Concat(_shell.State.Presets.Keys.Order()).ToList(); PresetBox.SelectedIndex = 0;
    }
    private void Preset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || PresetBox.SelectedItem is not string name) return;
        if (name == "默认参数") Form.ApplyValues(new());
        else if (_shell.State.Presets.TryGetValue(name, out var values)) Form.ApplyValues(values);
    }
    private void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        var name = Dialogs.Input(Window.GetWindow(this), "保存参数预设", "只保存普通参数。密码和 secret 字段不会写入文件。", "我的预设");
        if (name is null) return;
        if (name.Length > 64 || name is "当前参数" or "默认参数") { _shell.Notify("名称不可使用内置项，且最多 64 个字符。"); return; }
        if (_shell.State.Presets.ContainsKey(name) && !Dialogs.Confirm(Window.GetWindow(this), "覆盖同名预设？", name, "覆盖")) return;
        _shell.State.Presets[name] = Form.GetValues(); _shell.SaveState(); _loading = true; RefreshPresets(); PresetBox.SelectedItem = name; _loading = false; _shell.Notify("参数预设已保存。");
    }
    private void DeletePreset_Click(object sender, RoutedEventArgs e)
    {
        if (PresetBox.SelectedItem is not string name || !_shell.State.Presets.ContainsKey(name)) return;
        if (!Dialogs.Confirm(Window.GetWindow(this), "删除参数预设？", name, "删除", true)) return;
        _shell.State.Presets.Remove(name); _shell.SaveState(); _loading = true; RefreshPresets(); _loading = false;
    }
    private void History_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (HistoryList.SelectedItem is RunHistory record) Dialogs.Info(Window.GetWindow(this), "运行记录", $"{record.ActionLabel}\n{record.Started:yyyy-MM-dd HH:mm:ss}\n状态：{record.Status} / 退出码：{record.ExitCode}\n耗时：{record.DurationSeconds:0.0} 秒\n\n命令（脱敏）：\n{record.Command}\n\n磁盘日志：\n{record.LogPath}");
    }
    private void Shell_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.LogText) && AutoScrollCheck.IsChecked == true) Dispatcher.InvokeAsync(() => LogBox.ScrollToEnd());
        if (e.PropertyName == nameof(ShellViewModel.EnvironmentBadge)) RefreshPreview();
    }
    public void Dispose() => _shell.PropertyChanged -= Shell_PropertyChanged;
}
