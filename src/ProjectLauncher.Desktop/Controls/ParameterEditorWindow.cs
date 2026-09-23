using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ProjectLauncher.Core;

namespace ProjectLauncher.Desktop.Controls;

/// <summary>Detached parameter editor. Only Save invokes the caller's validated commit.</summary>
public sealed class ParameterEditorWindow : Window
{
    public TextBox LabelBox { get; } = new();
    public TextBox ArgumentBox { get; } = new();
    public TextBox DefaultBox { get; } = new();
    public ComboBox TypeBox { get; } = new() { ItemsSource = ConfigValidator.ParameterTypes };
    public Button SaveButton { get; } = new() { Content = "保存参数", IsDefault = true };
    public TextBlock ErrorText { get; } = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _binding = new() { ItemsSource = new[] { "argument", "positional", "env" } };
    private readonly TextBox _env = new(), _description = new(), _min = new(), _max = new(), _options = new(), _falseArgument = new(), _conditions = new();
    private readonly ComboBox _booleanMode = new() { ItemsSource = new[] { "flag", "value" } };
    private readonly CheckBox _required = new() { Content = "必填" }, _advanced = new() { Content = "高级参数" }, _mustExist = new() { Content = "路径必须存在" }, _secret = new() { Content = "敏感值（不保存默认值）" };
    private readonly ParameterDefinition _source;
    private readonly Action<ParameterDefinition> _save;
    private readonly StackPanel _specific = new();

    public ParameterEditorWindow(ParameterDefinition source, bool isNew, Action<ParameterDefinition> save)
    {
        _source = ActionEditing.CloneParameter(source); _save = save;
        Title = isNew ? "新增运行参数" : "编辑运行参数";
        Width = 660; Height = 600; MinWidth = 480; MinHeight = 440;
        MaxHeight = SystemParameters.WorkArea.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(StyleProperty, typeof(Window));
        var textStyle = new Style(typeof(TextBox), (Style)FindResource(typeof(TextBox)));
        textStyle.Setters.Add(new Setter(Control.MinHeightProperty, 34.0));
        textStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 6, 10, 6)));
        Resources.Add(typeof(TextBox), textStyle);
        var root = new Grid { Margin = new(24) };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var title = new StackPanel(); title.Children.Add(new TextBlock { Text = Title, FontSize = 22, FontWeight = FontWeights.SemiBold });
        title.Children.Add(Hint("仅用于当前启动动作 · 保存后立即显示在参数列表", new(0, 6, 0, 18))); root.Children.Add(title);
        var form = new StackPanel(); var scroll = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        LabelBox.Text = source.Label; ArgumentBox.Text = source.Argument; TypeBox.SelectedItem = source.Type;
        DefaultBox.Text = source.IsSecret ? "" : ValueCodec.Text(source.Default);
        _binding.SelectedItem = source.Binding; _env.Text = source.EnvName; _description.Text = source.Description;
        _min.Text = ValueCodec.Text(source.Min); _max.Text = ValueCodec.Text(source.Max);
        _options.Text = string.Join("\n", source.Options); _options.AcceptsReturn = true; _options.MinHeight = 80;
        _conditions.Text = JsonSerializer.Serialize(source.VisibleWhen); _conditions.AcceptsReturn = true; _conditions.TextWrapping = TextWrapping.Wrap;
        _booleanMode.SelectedItem = source.BooleanMode; _falseArgument.Text = source.FalseArgument;
        _required.IsChecked = source.Required; _advanced.IsChecked = source.Advanced; _mustExist.IsChecked = source.MustExist; _secret.IsChecked = source.Secret;
        form.Children.Add(Pair("参数名称", LabelBox, "控件类型", TypeBox));
        form.Children.Add(Pair("传参方式", _binding, "默认值", DefaultBox));
        form.Children.Add(_specific);
        var flags = new WrapPanel { Margin = new(0, 6, 0, 14) }; foreach (var c in new[] { _required, _advanced }) { c.Margin = new(0, 0, 24, 0); flags.Children.Add(c); } form.Children.Add(flags);
        var more = new StackPanel(); more.Children.Add(Field("说明", _description));
        _secret.Margin = new(0, 8, 0, 12); more.Children.Add(_secret);
        more.Children.Add(Field("显示条件 · JSON 对象", _conditions));
        more.Children.Add(Hint("传参方式：argument 为命令开关，positional 为位置参数，env 为环境变量。", new(0, 8, 0, 0)));
        form.Children.Add(new Expander { Header = "更多设置", Content = more });
        var footer = new StackPanel { Margin = new(0, 12, 0, 0) }; Grid.SetRow(footer, 2); root.Children.Add(footer);
        ErrorText.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush"); footer.Children.Add(ErrorText);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 12, 0, 0) };
        var cancel = new Button { Content = "取消", IsCancel = true, Margin = new(0, 0, 10, 0) }; buttons.Children.Add(cancel);
        SaveButton.SetResourceReference(StyleProperty, "PrimaryButton"); buttons.Children.Add(SaveButton); footer.Children.Add(buttons); Content = root;
        TypeBox.SelectionChanged += (_, _) => UpdateFields(); _binding.SelectionChanged += (_, _) => UpdateFields();
        _secret.Click += (_, _) => UpdateFields();
        SaveButton.Click += (_, _) => Save();
        Loaded += (_, _) => LabelBox.Focus(); UpdateFields();
    }
    private void UpdateFields()
    {
        _specific.Children.Clear();
        var binding = _binding.SelectedItem?.ToString(); var type = TypeBox.SelectedItem?.ToString();
        if (binding == "argument") _specific.Children.Add(Field("命令参数 · 例如 --port", ArgumentBox));
        else if (binding == "env") _specific.Children.Add(Field("环境变量名称", _env));
        else _specific.Children.Add(Hint("按列表顺序传递参数值，不附加开关名称。", new(0, 4, 0, 12)));
        if (type is "integer" or "number") _specific.Children.Add(Pair("最小值（可选）", _min, "最大值（可选）", _max));
        if (type == "select") _specific.Children.Add(Field("可选值 · 每行一个", _options));
        if (type == "boolean") _specific.Children.Add(Pair("布尔传参规则", _booleanMode, "关闭时的开关（可选）", _falseArgument));
        if (type is "file" or "directory") _specific.Children.Add(_mustExist);
        DefaultBox.IsEnabled = type != "password" && _secret.IsChecked != true;
        if (!DefaultBox.IsEnabled) DefaultBox.Text = "";
    }
    private void Save()
    {
        try
        {
            var p = ActionEditing.CloneParameter(_source);
            p.Label = LabelBox.Text.Trim(); if (p.Label.Length == 0) throw new ConfigException("请填写参数名称。");
            p.Type = TypeBox.SelectedItem?.ToString() ?? "text";
            p.Binding = _binding.SelectedItem?.ToString() ?? "argument"; p.Argument = ArgumentBox.Text.Trim(); p.EnvName = _env.Text.Trim();
            p.Description = _description.Text; p.Required = _required.IsChecked == true; p.Advanced = _advanced.IsChecked == true;
            p.Secret = _secret.IsChecked == true; p.MustExist = _mustExist.IsChecked == true;
            p.Default = p.IsSecret || DefaultBox.Text.Length == 0 ? null : p.Type switch {
                "integer" => long.TryParse(DefaultBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : throw new ConfigException("默认值必须是整数。"),
                "number" => Number(DefaultBox.Text), "boolean" => ValueCodec.Boolean(DefaultBox.Text), _ => DefaultBox.Text };
            p.Min = Number(_min.Text); p.Max = Number(_max.Text);
            p.Options = _options.Text.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
            p.BooleanMode = _booleanMode.SelectedItem?.ToString() ?? "flag"; p.FalseArgument = _falseArgument.Text;
            var conditions = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(_conditions.Text) ?? throw new ConfigException("条件必须为 JSON 对象。");
            if (conditions.Values.Any(v => v.ValueKind is JsonValueKind.Object or JsonValueKind.Array)) throw new ConfigException("条件比较值必须是标量。");
            p.VisibleWhen = conditions.ToDictionary(x => x.Key, x => ValueCodec.Unwrap(x.Value));
            _save(p); DialogResult = true;
        }
        catch (Exception ex) { ErrorText.Text = ex.Message; }
    }
    private static double? Number(string text) => string.IsNullOrWhiteSpace(text) ? null : double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? n : throw new ConfigException("请输入有限数字。");
    private static TextBlock Hint(string text, Thickness margin) { var label = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = margin }; label.SetResourceReference(StyleProperty, "Hint"); return label; }
    private static StackPanel Field(string label, UIElement input)
    {
        if (input is FrameworkElement element && element.Parent is Panel parent) parent.Children.Remove(input);
        var panel = new StackPanel { Margin = new(0, 0, 0, 12) };
        panel.Children.Add(Hint(label, new(0, 0, 0, 6))); panel.Children.Add(input); return panel;
    }
    private static Grid Pair(string firstLabel, UIElement first, string secondLabel, UIElement second)
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new() { Width = new(16) }); grid.ColumnDefinitions.Add(new());
        grid.Children.Add(Field(firstLabel, first)); var right = Field(secondLabel, second); Grid.SetColumn(right, 2); grid.Children.Add(right); return grid;
    }
}
