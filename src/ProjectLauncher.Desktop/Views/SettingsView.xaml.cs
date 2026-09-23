using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;
using ProjectLauncher.Core;
using ProjectLauncher.Desktop.Services;
using ProjectLauncher.Desktop.ViewModels;

namespace ProjectLauncher.Desktop.Views;

public sealed class ArgumentRow : ObservableObject
{
    private string _value = "";
    public string Value { get => _value; set => Set(ref _value, value); }
    public ArgumentRow(string value) => _value = value;
}

public partial class SettingsView : UserControl
{
    private readonly ShellViewModel _shell;
    private LauncherConfig _draft = new();
    private ActionDefinition? _action;
    private ParameterDefinition? _parameter;
    private readonly ObservableCollection<ArgumentRow> _arguments = [];
    private bool _loading = true, _editorLoading;
    public bool HasUnsavedChanges { get; private set; }
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public SettingsView(ShellViewModel shell)
    {
        InitializeComponent(); _shell = shell; DataContext = shell;
        ModeBox.ItemsSource = new[] { "venv", "uv", "existing" };
        ShellBox.ItemsSource = new[] { "auto", "powershell", "pwsh", "cmd" };
        ParamTypeBox.ItemsSource = ConfigValidator.ParameterTypes;
        BindingBox.ItemsSource = new[] { "argument", "positional", "env" };
        BooleanModeBox.ItemsSource = new[] { "flag", "value" };
        ArgvList.ItemsSource = _arguments;
        AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, _) => MarkDirty()));
        AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler((_, e) => { if (e.OriginalSource is ComboBox) MarkDirty(); }));
        AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler((_, _) => MarkDirty()));
        AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler((_, _) => MarkDirty()));
        LoadDraft(_shell.Config);
    }
    public void ReloadFromShell() => LoadDraft(_shell.Config);
    private void MarkDirty()
    {
        if (_loading || _editorLoading) return;
        HasUnsavedChanges = true; DirtyLabel.Text = "● 有未保存的修改 · 保存会校验并备份";
    }
    private void LoadDraft(LauncherConfig config)
    {
        _loading = true; _action = null; _parameter = null; _draft = ConfigStore.Clone(config);
        NameBox.Text = _draft.App.Name; DescriptionBox.Text = _draft.App.Description; VersionBox.Text = _draft.App.Version; OutputBox.Text = _draft.App.OutputDir;
        ModeBox.SelectedItem = _draft.Runtime.Mode; ShellBox.SelectedItem = _draft.Runtime.Shell;
        ProjectDirBox.Text = _draft.Runtime.ProjectDir; VenvBox.Text = _draft.Runtime.Venv;
        PythonBox.Text = _draft.Runtime.Python; RequirementsBox.Text = _draft.Runtime.Requirements;
        EnvBox.Text = JsonSerializer.Serialize(_draft.Runtime.Env, Pretty);
        ActionList.ItemsSource = _draft.Actions; ParameterList.ItemsSource = _draft.Parameters;
        ActionList.SelectedIndex = 0; ParameterList.SelectedIndex = _draft.Parameters.Count > 0 ? 0 : -1;
        _action = ActionList.SelectedItem as ActionDefinition; _parameter = ParameterList.SelectedItem as ParameterDefinition;
        LoadAction(); LoadParameter(); YamlBox.Text = ConfigStore.Serialize(_draft);
        HasUnsavedChanges = false; DirtyLabel.Text = "保存时校验配置并保留 .bak 备份"; _loading = false;
    }
    private void CommitBasics()
    {
        Dictionary<string, string> env;
        try { env = string.IsNullOrWhiteSpace(EnvBox.Text) ? new() : JsonSerializer.Deserialize<Dictionary<string, string>>(EnvBox.Text) ?? new(); }
        catch (JsonException e) { throw new ConfigException("附加环境变量不是有效的 JSON 字符串映射：" + e.Message); }
        _draft.App.Name = NameBox.Text.Trim(); _draft.App.Description = DescriptionBox.Text; _draft.App.Version = VersionBox.Text.Trim(); _draft.App.OutputDir = OutputBox.Text.Trim();
        _draft.Runtime.Mode = ModeBox.SelectedItem?.ToString() ?? "venv"; _draft.Runtime.Shell = ShellBox.SelectedItem?.ToString() ?? "auto";
        _draft.Runtime.ProjectDir = ProjectDirBox.Text.Trim(); _draft.Runtime.Venv = VenvBox.Text.Trim();
        _draft.Runtime.Python = PythonBox.Text.Trim(); _draft.Runtime.Requirements = RequirementsBox.Text.Trim(); _draft.Runtime.Env = env;
    }
    private void CommitAction()
    {
        if (_action is null) return;
        if (!int.TryParse(TimeoutBox.Text.Trim(), out int timeout)) throw new ConfigException("超时秒数需要填写整数，0 表示不限时。");
        _action.Id = ActionIdBox.Text.Trim(); _action.Label = ActionLabelBox.Text.Trim();
        _action.Argv = _arguments.Select(a => a.Value).ToList();
        _action.Parameters = AllParametersCheck.IsChecked == true ? null : ActionParametersBox.Text.Split([',', '，', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        _action.TimeoutSeconds = timeout;
    }
    private void LoadAction()
    {
        _editorLoading = true; _arguments.Clear();
        if (_action is not null)
        {
            ActionIdBox.Text = _action.Id; ActionLabelBox.Text = _action.Label;
            foreach (var arg in _action.Argv) _arguments.Add(new(arg));
            AllParametersCheck.IsChecked = _action.Parameters is null;
            ActionParametersBox.Text = _action.Parameters is null ? "" : string.Join(", ", _action.Parameters);
            ActionParametersBox.IsEnabled = _action.Parameters is not null; TimeoutBox.Text = _action.TimeoutSeconds.ToString(CultureInfo.InvariantCulture);
        }
        _editorLoading = false;
    }
    private void CommitParameter()
    {
        if (_parameter is null) return;
        var type = ParamTypeBox.SelectedItem?.ToString() ?? "text";
        var minimum = ParseNumber(MinBox.Text, "最小值"); var maximum = ParseNumber(MaxBox.Text, "最大值");
        object? defaultValue = DefaultBox.Text.Length == 0 ? null : type switch
        {
            "boolean" => ValueCodec.Boolean(DefaultBox.Text),
            "integer" => long.TryParse(DefaultBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : throw new ConfigException("整数参数的默认值必须为整数。"),
            "number" => ParseNumber(DefaultBox.Text, "默认值"),
            _ => DefaultBox.Text
        };
        var conditions = new Dictionary<string, object?>();
        var displayedConditions = string.Join("\n", _parameter.VisibleWhen.Select(pair => pair.Key + "=" + ValueCodec.Text(pair.Value)));
        if (ConditionsBox.Text.Replace("\r\n", "\n") == displayedConditions)
            conditions = new(_parameter.VisibleWhen); // Preserve types / whitespace unless the user changes the editor.
        else
            foreach (var line in ConditionsBox.Text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                int at = line.IndexOf('='); if (at < 1) throw new ConfigException("显示条件需要使用 参数名=期望值 格式。");
                var key = line[..at].Trim(); var value = line[(at + 1)..].Trim();
                if (!conditions.TryAdd(key, value)) throw new ConfigException("显示条件重复：" + key);
            }
        var oldName = _parameter.Name; var newName = ParamNameBox.Text.Trim();
        if (newName != oldName && _draft.Parameters.Any(p => p != _parameter && p.Name == newName)) throw new ConfigException("参数名已存在：" + newName);
        if (newName != oldName)
        {
            foreach (var action in _draft.Actions) if (action.Parameters is not null) for (int i = 0; i < action.Parameters.Count; i++) if (action.Parameters[i] == oldName) action.Parameters[i] = newName;
            foreach (var param in _draft.Parameters) if (param != _parameter && param.VisibleWhen.Remove(oldName, out var value)) param.VisibleWhen[newName] = value;
            if (_action?.Parameters is not null) ActionParametersBox.Text = string.Join(", ", _action.Parameters);
        }
        _parameter.Name = newName; _parameter.Label = ParamLabelBox.Text.Trim(); _parameter.Type = type;
        _parameter.Binding = BindingBox.SelectedItem?.ToString() ?? "argument"; _parameter.Argument = ArgumentBox.Text.Trim(); _parameter.EnvName = EnvNameBox.Text.Trim();
        _parameter.Default = defaultValue; _parameter.Description = ParamDescriptionBox.Text;
        _parameter.Required = RequiredCheck.IsChecked == true; _parameter.Advanced = AdvancedParamCheck.IsChecked == true;
        _parameter.MustExist = MustExistCheck.IsChecked == true; _parameter.Secret = SecretCheck.IsChecked == true;
        _parameter.Min = minimum; _parameter.Max = maximum;
        if (OptionsBox.Text.Replace("\r\n", "\n") != string.Join("\n", _parameter.Options))
            _parameter.Options = OptionsBox.Text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        _parameter.BooleanMode = BooleanModeBox.SelectedItem?.ToString() ?? "flag"; _parameter.FalseArgument = FalseArgumentBox.Text.Trim(); _parameter.VisibleWhen = conditions;
    }
    private void LoadParameter()
    {
        _editorLoading = true; ParameterEditor.IsEnabled = _parameter is not null;
        if (_parameter is { } p)
        {
            ParamNameBox.Text = p.Name; ParamLabelBox.Text = p.Label; ParamTypeBox.SelectedItem = p.Type; BindingBox.SelectedItem = p.Binding;
            ArgumentBox.Text = p.Argument; EnvNameBox.Text = p.EnvName; DefaultBox.Text = ValueCodec.Text(p.Default); ParamDescriptionBox.Text = p.Description;
            RequiredCheck.IsChecked = p.Required; AdvancedParamCheck.IsChecked = p.Advanced; MustExistCheck.IsChecked = p.MustExist; SecretCheck.IsChecked = p.Secret;
            MinBox.Text = p.Min?.ToString(CultureInfo.InvariantCulture) ?? ""; MaxBox.Text = p.Max?.ToString(CultureInfo.InvariantCulture) ?? "";
            OptionsBox.Text = string.Join("\n", p.Options); BooleanModeBox.SelectedItem = p.BooleanMode; FalseArgumentBox.Text = p.FalseArgument;
            ConditionsBox.Text = string.Join("\n", p.VisibleWhen.Select(pair => pair.Key + "=" + ValueCodec.Text(pair.Value)));
        }
        else
        {
            ParamNameBox.Text = ParamLabelBox.Text = DefaultBox.Text = ParamDescriptionBox.Text = "";
        }
        _editorLoading = false;
    }
    private void CommitEditors() { CommitBasics(); CommitAction(); CommitParameter(); ActionList.Items.Refresh(); ParameterList.Items.Refresh(); }
    private static double? ParseNumber(string text, string label)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value)) throw new ConfigException(label + "必须是有限数字，小数点使用 .。");
        return value;
    }
    private void ActionSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _editorLoading) return;
        try { CommitAction(); _action = ActionList.SelectedItem as ActionDefinition; LoadAction(); }
        catch (Exception ex) { _shell.Notify(ex.Message); _editorLoading = true; ActionList.SelectedItem = _action; _editorLoading = false; }
    }
    private void ParameterSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _editorLoading) return;
        try { CommitParameter(); _parameter = ParameterList.SelectedItem as ParameterDefinition; LoadParameter(); }
        catch (Exception ex) { _shell.Notify(ex.Message); _editorLoading = true; ParameterList.SelectedItem = _parameter; _editorLoading = false; }
    }
    private void AllParameters_Changed(object sender, RoutedEventArgs e) { if (ActionParametersBox is not null) ActionParametersBox.IsEnabled = AllParametersCheck.IsChecked != true; }
    private void AddArg_Click(object sender, RoutedEventArgs e) { _arguments.Add(new("")); MarkDirty(); }
    private void RemoveArg_Click(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.Tag is ArgumentRow row) _arguments.Remove(row); MarkDirty(); }
    private void ArgUp_Click(object sender, RoutedEventArgs e) => MoveArgument(-1);
    private void ArgDown_Click(object sender, RoutedEventArgs e) => MoveArgument(1);
    private void MoveArgument(int delta) { int i = ArgvList.SelectedIndex; if (i < 0 || i + delta < 0 || i + delta >= _arguments.Count) return; _arguments.Move(i, i + delta); ArgvList.SelectedIndex = i + delta; MarkDirty(); }
    private void AddAction_Click(object sender, RoutedEventArgs e) => TryEdit(() =>
    {
        CommitAction(); var action = new ActionDefinition { Id = Unique("action", _draft.Actions.Select(a => a.Id)), Label = "新的启动动作", Parameters = [] };
        _draft.Actions.Add(action); ActionList.Items.Refresh(); ActionList.SelectedItem = action; MarkDirty();
    });
    private void DeleteAction_Click(object sender, RoutedEventArgs e) => TryEdit(() =>
    {
        if (_action is null) return; if (_draft.Actions.Count == 1) throw new ConfigException("至少需要保留一个启动动作。");
        if (!Dialogs.Confirm(Window.GetWindow(this), "删除启动动作？", _action.Label, "删除", true)) return;
        var selected = _action; _action = null; _draft.Actions.Remove(selected); ActionList.Items.Refresh(); ActionList.SelectedIndex = 0; MarkDirty();
    });
    private void AddParameter_Click(object sender, RoutedEventArgs e) => TryEdit(() =>
    {
        CommitEditors(); var name = Unique("parameter", _draft.Parameters.Select(p => p.Name));
        var p = new ParameterDefinition { Name = name, Label = "新的参数", Argument = "--" + name };
        _draft.Parameters.Add(p); ParameterList.Items.Refresh(); ParameterList.SelectedItem = p; MarkDirty();
    });
    private void DuplicateParameter_Click(object sender, RoutedEventArgs e) => TryEdit(() =>
    {
        if (_parameter is null) return; CommitEditors();
        // Clone through the complete validated schema so nested collections are independent.
        var validated = ConfigStore.Clone(_draft); var copy = validated.Parameters.First(p => p.Name == _parameter.Name);
        copy.Name = Unique(copy.Name + "_copy", _draft.Parameters.Select(p => p.Name)); copy.Label += "（副本）";
        _draft.Parameters.Add(copy); ParameterList.Items.Refresh(); ParameterList.SelectedItem = copy; MarkDirty();
    });
    private void DeleteParameter_Click(object sender, RoutedEventArgs e) => TryEdit(() =>
    {
        if (_parameter is null) return;
        if (!Dialogs.Confirm(Window.GetWindow(this), "删除参数？", $"{_parameter.DisplayName}\n\n该参数在启动动作中的引用、以及其他参数对它的显示条件也会移除。", "删除", true)) return;
        var selected = _parameter; _parameter = null; _draft.Parameters.Remove(selected);
        foreach (var action in _draft.Actions) action.Parameters?.RemoveAll(n => n == selected.Name);
        foreach (var p in _draft.Parameters) p.VisibleWhen.Remove(selected.Name);
        ParameterList.Items.Refresh(); ParameterList.SelectedIndex = _draft.Parameters.Count > 0 ? 0 : -1; LoadParameter(); LoadAction(); MarkDirty();
    });
    private void ParameterUp_Click(object sender, RoutedEventArgs e) => MoveParameter(-1);
    private void ParameterDown_Click(object sender, RoutedEventArgs e) => MoveParameter(1);
    private void MoveParameter(int delta) => TryEdit(() =>
    {
        CommitParameter(); int i = ParameterList.SelectedIndex; if (i < 0 || i + delta < 0 || i + delta >= _draft.Parameters.Count) return;
        var p = _draft.Parameters[i]; _draft.Parameters.RemoveAt(i); _draft.Parameters.Insert(i + delta, p); ParameterList.Items.Refresh(); ParameterList.SelectedItem = p; MarkDirty();
    });
    private static string Unique(string basis, IEnumerable<string> names) { var used = names.ToHashSet(); var value = basis; int n = 2; while (used.Contains(value)) value = basis + n++; return value; }
    private void GenerateYaml_Click(object sender, RoutedEventArgs e) => TryEdit(() => { CommitEditors(); YamlBox.Text = ConfigStore.Serialize(_draft); MarkDirty(); });
    private void ApplyYaml_Click(object sender, RoutedEventArgs e) => TryEdit(() =>
    {
        var parsed = ConfigStore.Parse(YamlBox.Text);
        if (!Dialogs.Confirm(Window.GetWindow(this), "用 YAML 更新设置表单？", "设置表单中的未保存内容将由当前 YAML 替换。此操作暂时不写入磁盘。", "更新表单")) return;
        LoadDraft(parsed); MarkDirty();
    });
    private void Save_Click(object sender, RoutedEventArgs e) => TryEdit(() =>
    {
        LauncherConfig config;
        if (SettingTabs.SelectedIndex == 3) config = ConfigStore.Parse(YamlBox.Text);
        else { CommitEditors(); ConfigValidator.Validate(_draft); config = _draft; }
        _shell.ApplyConfiguration(config); LoadDraft(_shell.Config);
    });
    private void Reload_Click(object sender, RoutedEventArgs e) => TryEdit(() =>
    {
        if (HasUnsavedChanges && !Dialogs.Confirm(Window.GetWindow(this), "放弃未保存的修改？", "会从磁盘重新读取 launcher.yaml。", "放弃并重新加载", true)) return;
        _shell.ReloadConfiguration(); LoadDraft(_shell.Config);
    });
    private void BrowseProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择业务项目目录" }; if (dialog.ShowDialog(Window.GetWindow(this)) == true) ProjectDirBox.Text = dialog.FolderName;
    }
    private void BrowseVenv_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择项目虚拟环境（例如 .venv）" }; if (dialog.ShowDialog(Window.GetWindow(this)) == true) VenvBox.Text = dialog.FolderName;
    }
    private void TryEdit(Action action) { try { action(); } catch (Exception e) { _shell.Notify(e.Message); } }
    public void DiscardDirtyMarker() { HasUnsavedChanges = false; }
    public void SelectDesigner() => SettingTabs.SelectedIndex = 2;
    public void SelectYaml() => SettingTabs.SelectedIndex = 3;
}
