using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using ProjectLauncher.Core;
using ProjectLauncher.Desktop.Services;

namespace ProjectLauncher.Desktop.Controls;

/// <summary>A draft-only editor. It never executes project commands or writes configuration.</summary>
public sealed class ActionWorkspace : UserControl
{
    private readonly LauncherConfig _draft;
    private readonly string _root;
    private readonly Action _changed;
    private readonly ListBox _actions = new() { Name = "SettingsActionList" };
    private readonly StackPanel _editor = new();
    public event Action<string?>? ValidationNotice;
    private readonly Dictionary<Control, string> _errors = new();
    private ActionDefinition? _action;
    private bool _building;
    private bool _queued;
    public string SelectedActionId => _action?.Id ?? "";

    public ActionWorkspace(LauncherConfig draft, string root, Action changed)
    {
        _draft = draft; _root = root; _changed = changed;
        var grid = new Grid(); grid.ColumnDefinitions.Add(new() { Width = new(184) });
        grid.ColumnDefinitions.Add(new() { Width = new(12) }); grid.ColumnDefinitions.Add(new());
        grid.RowDefinitions.Add(new());
        var listPanel = new Grid(); listPanel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        listPanel.RowDefinitions.Add(new()); listPanel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var listTitle = Label("启动动作", true); listTitle.Margin = new(0, 0, 0, 10); listPanel.Children.Add(listTitle);
        ScrollViewer.SetVerticalScrollBarVisibility(_actions, ScrollBarVisibility.Auto);
        var itemText = new FrameworkElementFactory(typeof(TextBlock));
        itemText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("."));
        itemText.SetBinding(ToolTipProperty, new System.Windows.Data.Binding("."));
        itemText.SetValue(TextBlock.TextWrappingProperty, TextWrapping.NoWrap);
        itemText.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        itemText.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { RelativeSource = new(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(ListBoxItem), 1) });
        _actions.ItemTemplate = new DataTemplate { VisualTree = itemText };
        Grid.SetRow(_actions, 1); listPanel.Children.Add(_actions);
        var buttons = new StackPanel { Margin = new(0, 12, 0, 0) };
        var add = Button("＋ 添加动作", AddAction); add.Margin = new(0, 0, 0, 6); buttons.Children.Add(add);
        var delete = Button("删除动作", DeleteAction); delete.Margin = new(0); buttons.Children.Add(delete);
        Grid.SetRow(buttons, 2); listPanel.Children.Add(buttons);
        var listCard = Card(listPanel); listCard.Padding = new(10, 14, 10, 10); grid.Children.Add(listCard);
        var card = Card(new ScrollViewer { Content = _editor, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Grid.SetColumn(card, 2); grid.Children.Add(card);
        Content = grid;
        _actions.SelectionChanged += (_, _) => { if (_building) return; if (!CanRebuild()) { _building = true; _actions.SelectedItem = _action; _building = false; return; } _action = _actions.SelectedItem as ActionDefinition; Rebuild(); };
        RefreshActions(draft.Actions.FirstOrDefault());
    }
    private static Border Card(UIElement content)
    { var b = new Border { Child = content, Padding = new(14) }; b.SetResourceReference(StyleProperty, "Card"); return b; }
    private static TextBlock Label(string text, bool title = false)
    { var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new(0, title ? 10 : 4, 0, 7) }; t.SetResourceReference(StyleProperty, title ? "FieldLabel" : "Hint"); return t; }
    private Button Button(string text, Action click)
    { var b = new Button { Content = text, Padding = new(10, 6, 10, 6), MinHeight = 32, Margin = new(4, 0, 0, 0) }; b.Click += (_, _) => { if (!CanRebuild()) return; try { click(); } catch (Exception ex) { ValidationNotice?.Invoke(ex.Message); } }; return b; }
    private bool CanRebuild()
    {
        if (_errors.Count == 0) return true;
        var first = _errors.First(); ValidationNotice?.Invoke(first.Value);
        first.Key.BringIntoView(); first.Key.Focus(); return false;
    }
    private TextBox Text(string value, Action<string> write, bool multiline = false)
    {
        var box = new TextBox { Text = value, MinHeight = 34, Padding = new(9, 6, 9, 6), AcceptsReturn = multiline, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 8) };
        box.TextChanged += (_, _) => Edit(box, () => write(box.Text)); return box;
    }
    private ComboBox Choice(IEnumerable<string> choices, string selected, Action<string> write)
    {
        var box = new ComboBox { ItemsSource = choices, SelectedItem = selected, MinHeight = 34, Margin = new(0, 0, 0, 8) };
        box.SelectionChanged += (_, _) => {
            if (_building) return;
            if (!CanRebuild()) { _building = true; box.SelectedItem = selected; _building = false; return; }
            Edit(box, () => write(box.SelectedItem?.ToString() ?? "")); selected = box.SelectedItem?.ToString() ?? "";
        }; return box;
    }
    private void Edit(Control field, Action write)
    {
        if (_building) return;
        try { write(); _errors.Remove(field); field.ToolTip = null; }
        catch (Exception ex) { _errors[field] = ex.Message; field.ToolTip = ex.Message; }
        _changed(); QueuePreview();
    }
    private void QueuePreview()
    {
        if (_queued) return; _queued = true;
        Dispatcher.InvokeAsync(() => { _queued = false; RefreshPreview(); }, DispatcherPriority.Background);
    }
    public void Commit()
    {
        if (_errors.Count != 0) throw new ConfigException(_errors.First().Value);
        foreach (var action in _draft.Actions)
            if (ActionEditing.CommandKind(action) is "script" or "module" && string.IsNullOrWhiteSpace(action.Argv.Last())) throw new ConfigException("请填写动作的执行目标。");
        ConfigValidator.Validate(_draft);
    }
    public void SelectAction(string id) { if (CanRebuild()) RefreshActions(_draft.Actions.FirstOrDefault(a => a.Id == id) ?? _draft.Actions.FirstOrDefault()); }
    private void RefreshActions(ActionDefinition? action)
    {
        _building = true; _actions.ItemsSource = null; _actions.ItemsSource = _draft.Actions; _actions.SelectedItem = action; _action = action; _building = false; Rebuild();
    }
    private void AddAction()
    {
        if (_errors.Count != 0) { ValidationNotice?.Invoke("请先修正当前字段。"); return; }
        RefreshActions(ActionEditing.AddAction(_draft)); _changed();
    }
    private void DeleteAction()
    {
        if (_action is null || !Dialogs.Confirm(Window.GetWindow(this), "删除动作？", _action.ToString(), "删除", true)) return;
        _draft.Actions.Remove(_action); RefreshActions(_draft.Actions.FirstOrDefault()); _changed();
    }
    private bool Membership()
    {
        if (!_draft.Actions.Any(a => a.Parameters is null)) return true;
        if (!Dialogs.Confirm(Window.GetWindow(this), "固定当前参数成员？", "旧配置中有动作使用全部参数。为避免新增或移除参数影响其他动作，将把这些动作转换为当前参数的明确列表；以后新增参数只绑定当前动作。", "转换并继续")) return false;
        ActionEditing.ExplicitMembership(_draft, true); return true;
    }
    private void Rebuild()
    {
        if (!CanRebuild()) return;
        _building = true; _errors.Clear(); _editor.Children.Clear();
        if (_action is null) _editor.Children.Add(Label("尚未添加启动动作。点击上方「添加动作」开始。"));
        else
        {
            var action = _action;
            var nameBox = Text(action.Label, v => { action.Label = v; _building = true; _actions.ItemsSource = null; _actions.ItemsSource = _draft.Actions; _actions.SelectedItem = action; _building = false; });
            var kinds = new[] { "Python 脚本", "Python 模块", "其他程序", "高级命令" };
            var kind = ActionEditing.CommandKind(action); int index = Array.IndexOf(new[] { "script", "module", "program", "advanced" }, kind);
            var kindBox = Choice(kinds, kinds[index], v => {
                if (v == kinds[index]) return;
                if (!Dialogs.Confirm(Window.GetWindow(this), "更换启动方式？", "将替换当前命令目标与固定参数，表单参数保持不变。", "更换")) { Dispatcher.InvokeAsync(Rebuild); return; }
                action.Argv = v == kinds[0] ? ["python", ""] : v == kinds[1] ? ["python", "-m", ""] : [""];
                if (v == kinds[3]) action.Argv = ["python", "-c", ""];
                Dispatcher.InvokeAsync(Rebuild);
            });
            _editor.Children.Add(Pair("动作名称", nameBox, "启动方式", kindBox));
            if (kind == "advanced")
            {
                _editor.Children.Add(Label("固定命令参数 · 每行一个 argv，不添加引号"));
                _editor.Children.Add(Text(string.Join("\n", action.Argv), v => action.Argv = v.Replace("\r\n", "\n").Split('\n').ToList(), true));
            }
            else
            {
                _editor.Children.Add(Label(kind == "module" ? "模块名称" : "执行目标"));
                int target = kind == "script" ? 1 : kind == "module" ? 2 : 0;
                var box = Text(action.Argv[target], v => action.Argv[target] = v);
                var line = new DockPanel();
                if (kind != "module") { var browse = Button("浏览…", () => { var root = new ProjectEnvironment(_draft, Path.Combine(_root, "launcher.yaml")).Root; var d = new OpenFileDialog { InitialDirectory = root, Filter = kind == "script" ? "Python|*.py|所有文件|*.*" : "程序|*.exe|所有文件|*.*" }; if (d.ShowDialog(Window.GetWindow(this)) == true) box.Text = ActionEditing.BrowsedTarget(root, d.FileName); }); DockPanel.SetDock(browse, Dock.Right); line.Children.Add(browse); }
                line.Children.Add(box); _editor.Children.Add(line);
            }
            var parameterHeading = new DockPanel { Margin = new(0, 8, 0, 10) };
            var addParameter = Button("＋ 添加参数", () => EditParameter(null, action));
            DockPanel.SetDock(addParameter, Dock.Right); parameterHeading.Children.Add(addParameter);
            var heading = Label("运行参数", true); heading.Margin = new(0); heading.VerticalAlignment = VerticalAlignment.Center;
            parameterHeading.Children.Add(heading); _editor.Children.Add(parameterHeading);
            foreach (var p in CommandBuilder.SelectedParameters(_draft, action).ToArray()) AddParameterRow(p, action);
            var advanced = new StackPanel(); advanced.Children.Add(Label("内部 ID（自动生成）")); advanced.Children.Add(new TextBox { Text = action.Id, IsReadOnly = true });
            advanced.Children.Add(Label("超时秒数 · 0 不限时")); advanced.Children.Add(Text(action.TimeoutSeconds.ToString(), v => action.TimeoutSeconds = int.TryParse(v, out int n) && n >= 0 ? n : throw new ConfigException("超时必须为非负整数。")));
            _editor.Children.Add(new Expander { Header = "动作高级设置", Content = advanced, Margin = new(0, 12, 0, 0) });
        }
        _building = false; RefreshPreview();
    }
    private void EditParameter(ParameterDefinition? parameter, ActionDefinition action)
    {
        var dialog = new ParameterEditorWindow(parameter ?? new() { Label = "新的参数", Argument = "--parameter" }, parameter is null, edited => {
            bool needsConfirmation = _draft.Actions.Any(a => a.Parameters is null);
            if (needsConfirmation && !Dialogs.Confirm(Window.GetWindow(this), "固定当前参数成员？", "将旧配置的全部参数成员固定为当前列表，后续修改仅影响当前动作。", "转换并保存"))
                throw new ConfigException("未保存：参数成员转换已取消。");
            ActionEditing.SaveParameter(_draft, action, parameter?.Name, edited, needsConfirmation);
        }) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true) { _changed(); Rebuild(); }
    }
    private void AddParameterRow(ParameterDefinition p, ActionDefinition action)
    {
        var row = new DockPanel { Margin = new(0, 0, 0, 8) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(Button("编辑", () => EditParameter(p, action)));
        buttons.Children.Add(Button("↑", () => Move(p, -1))); buttons.Children.Add(Button("↓", () => Move(p, 1)));
        buttons.Children.Add(Button("移除", () => { if (!Membership()) return; action.Parameters!.Remove(p.Name); _changed(); Rebuild(); }));
        DockPanel.SetDock(buttons, Dock.Right); row.Children.Add(buttons);
        var info = new StackPanel { Margin = new(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock { Text = p.DisplayName, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        string binding = p.Binding == "env" ? "ENV " + p.EnvName : p.Binding == "positional" ? "位置参数" : p.Argument;
        string value = p.IsSecret ? "运行时输入" : p.Default is null ? "未设默认值" : "默认：" + ValueCodec.Text(p.Default);
        var detail = Label(binding + "  ·  " + p.Type + "  ·  " + value + (p.Required ? "  ·  必填" : ""));
        detail.TextWrapping = TextWrapping.NoWrap; detail.TextTrimming = TextTrimming.CharacterEllipsis;
        info.Children.Add(detail); row.Children.Add(info);
        var card = Card(row); card.Padding = new(12, 10, 12, 2); card.Margin = new(0, 0, 0, 8); _editor.Children.Add(card);
    }
    private void Move(ParameterDefinition p, int delta)
    {
        if (_action is null || !Membership()) return;
        var list = _action.Parameters!; int i = list.IndexOf(p.Name), j = i + delta;
        if (i < 0 || j < 0 || j >= list.Count) return;
        list.RemoveAt(i); list.Insert(j, p.Name); _changed(); Rebuild();
    }
    private static Grid Pair(string firstLabel, UIElement first, string secondLabel, UIElement second)
    {
        var row = new Grid(); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = new(10) }); row.ColumnDefinitions.Add(new());
        var left = new StackPanel(); left.Children.Add(Label(firstLabel)); left.Children.Add(first); row.Children.Add(left);
        var right = new StackPanel(); right.Children.Add(Label(secondLabel)); right.Children.Add(second); Grid.SetColumn(right, 2); row.Children.Add(right);
        return row;
    }
    private void RefreshPreview()
    {
        // Validation feedback only; settings no longer hosts a runnable form or draft preview.
        try { Commit(); ValidationNotice?.Invoke(null); }
        catch (ConfigException) { /* Report only when an operation is blocked, not on every keystroke. */ }
    }
}
