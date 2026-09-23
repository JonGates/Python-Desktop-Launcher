using ProjectLauncher.Desktop.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using ProjectLauncher.Core;

namespace ProjectLauncher.Desktop.Controls;

public sealed class ParameterForm : UserControl
{
    private readonly Grid _root = new();
    private readonly Dictionary<string, Field> _fields = new();
    private LauncherConfig? _config;
    private Dictionary<string, object?> _allValues = new();
    private string _projectRoot = "";
    private bool _updating;
    private bool _showAdvanced;
    public bool ShowAdvanced { get => _showAdvanced; set { _showAdvanced = value; UpdateVisibility(); } }
    public event Action? ValuesChanged;
    private sealed record Field(ParameterDefinition Definition, FrameworkElement Container, Func<object?> Read, Action<object?> Write);
    public ParameterForm() { Content = _root; SizeChanged += (_, _) => LayoutFields(); }
    private void LayoutFields()
    {
        _root.RowDefinitions.Clear(); _root.ColumnDefinitions.Clear();
        int columns = ActualWidth >= 600 ? 2 : 1;
        for (int i = 0; i < columns; i++) _root.ColumnDefinitions.Add(new());
        int row = 0, column = 0;
        foreach (var field in _fields.Values.Where(f => f.Container.Visibility == Visibility.Visible))
        {
            bool wide = columns == 1 || field.Definition.Type is "file" or "directory" or "textarea";
            if (wide && column != 0) { row++; column = 0; }
            while (_root.RowDefinitions.Count <= row) _root.RowDefinitions.Add(new() { Height = GridLength.Auto });
            Grid.SetRow(field.Container, row); Grid.SetColumn(field.Container, column);
            Grid.SetColumnSpan(field.Container, wide ? columns : 1);
            field.Container.Margin = new(0, 0, !wide && column == 0 ? 16 : 0, 10);
            if (wide || ++column == columns) { row++; column = 0; }
        }
    }

    public void Configure(LauncherConfig config, ActionDefinition action, Dictionary<string, object?> values, string projectRoot)
    {
        _updating = true; _config = config; _allValues = CommandBuilder.EffectiveValues(config, values); _projectRoot = projectRoot;
        _fields.Clear(); _root.Children.Clear();
        foreach (var parameter in CommandBuilder.SelectedParameters(config, action)) CreateField(parameter);
        if (_fields.Count == 0) _root.Children.Add(new TextBlock { Text = "", Style = (Style)FindResource("Hint"), Margin = new Thickness(2, 10, 2, 14) }.Localize(TextBlock.TextProperty, "Text.099"));
        _updating = false; UpdateVisibility();
    }
    private void CreateField(ParameterDefinition p)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 5) };
        var type = new TextBlock { Text = p.Binding == "env" ? "ENV" : p.Advanced ? "ADVANCED" : p.Type.ToUpperInvariant(), Style = (Style)FindResource("Micro"), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(type, Dock.Right); heading.Children.Add(type);
        heading.Children.Add(new TextBlock { Text = p.DisplayName + (p.Required ? " *" : ""), FontWeight = FontWeights.SemiBold, FontSize = 13 });
        panel.Children.Add(heading);
        object? initial = _allValues.GetValueOrDefault(p.Name);
        Func<object?> read; Action<object?> write;
        if (p.Type == "boolean")
        {
            var check = new CheckBox { Content = "", IsChecked = TryBool(initial), Margin = new Thickness(0, 5, 0, 5) }.Localize(ContentControl.ContentProperty, "Text.100");
            check.Checked += Changed; check.Unchecked += Changed; panel.Children.Add(check);
            read = () => check.IsChecked == true; write = v => check.IsChecked = TryBool(v);
        }
        else if (p.Type == "select")
        {
            var select = new ComboBox { ItemsSource = p.Options, SelectedItem = ValueCodec.Text(initial) };
            select.SelectionChanged += (_, _) => Changed(); panel.Children.Add(select);
            read = () => select.SelectedItem?.ToString() ?? ""; write = v => select.SelectedItem = ValueCodec.Text(v);
        }
        else if (p.IsSecret)
        {
            var password = new PasswordBox { Password = ValueCodec.Text(initial), MaxLength = 8192 };
            password.PasswordChanged += (_, _) => Changed(); panel.Children.Add(password);
            read = () => password.Password; write = v => password.Password = ValueCodec.Text(v);
        }
        else
        {
            var text = new TextBox { Text = ValueCodec.Text(initial), MaxLength = 65536 };
            if (p.Type == "textarea") { text.AcceptsReturn = true; text.TextWrapping = TextWrapping.Wrap; text.MinHeight = 88; text.MaxHeight = 180; text.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; }
            text.TextChanged += (_, _) => Changed();
            if (p.Type is "file" or "directory")
            {
                var row = new Grid(); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                row.Children.Add(text);
                var browse = new Button { Content = "", Margin = new Thickness(8, 0, 0, 0), MinWidth = 65, Padding = new Thickness(12, 8, 12, 8) }.Localize(ContentControl.ContentProperty, "Text.101");
                browse.Click += (_, _) => Browse(p, text); Grid.SetColumn(browse, 1); row.Children.Add(browse); panel.Children.Add(row);
                text.AllowDrop = true;
                text.PreviewDragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
                text.Drop += (_, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) text.Text = files[0]; e.Handled = true; };
            }
            else panel.Children.Add(text);
            read = () => text.Text; write = v => text.Text = ValueCodec.Text(v);
        }
        var description = p.Description;
        if (p.IsSecret) description += (description.Length > 0 ? "\n" : "") + L.Text("Text.102");
        if (description.Length > 0) panel.Children.Add(new TextBlock { Style = (Style)FindResource("Hint"), Margin = new Thickness(0, 6, 0, 0) }
            .Dynamic(TextBlock.TextProperty, () => p.Description + (p.IsSecret ? (p.Description.Length > 0 ? "\n" : "") + L.Text("Text.102") : "")));
        _fields[p.Name] = new(p, panel, read, write); _root.Children.Add(panel);
    }
    private void Browse(ParameterDefinition p, TextBox target)
    {
        string initial = _projectRoot;
        try { var path = Path.GetFullPath(target.Text, _projectRoot); initial = Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? initial; } catch { }
        var owner = Window.GetWindow(this);
        if (p.Type == "directory")
        {
            var dialog = new OpenFolderDialog { Title = p.DisplayName, InitialDirectory = initial };
            if (dialog.ShowDialog(owner) == true) target.Text = dialog.FolderName;
        }
        else if (p.MustExist)
        {
            var dialog = new OpenFileDialog { Title = p.DisplayName, InitialDirectory = initial, CheckFileExists = true };
            if (dialog.ShowDialog(owner) == true) target.Text = dialog.FileName;
        }
        else
        {
            var dialog = new SaveFileDialog { Title = p.DisplayName, InitialDirectory = initial, OverwritePrompt = false };
            if (dialog.ShowDialog(owner) == true) target.Text = dialog.FileName;
        }
    }
    private void Changed(object? sender = null, RoutedEventArgs? e = null)
    {
        if (_updating) return;
        foreach (var (name, field) in _fields) _allValues[name] = field.Read();
        UpdateVisibility(); ValuesChanged?.Invoke();
    }
    private void UpdateVisibility()
    {
        if (_config is null) return;
        var values = CommandBuilder.EffectiveValues(_config, _allValues);
        foreach (var field in _fields.Values) field.Container.Visibility = (ShowAdvanced || !field.Definition.Advanced) && CommandBuilder.IsVisible(field.Definition, values) ? Visibility.Visible : Visibility.Collapsed;
        LayoutFields();
    }
    public Dictionary<string, object?> GetValues()
    {
        foreach (var (name, field) in _fields) _allValues[name] = field.Read();
        return new(_allValues);
    }
    public void ApplyValues(Dictionary<string, object?> values)
    {
        if (_config is null) return;
        _updating = true; _allValues = CommandBuilder.EffectiveValues(_config, values);
        foreach (var (name, field) in _fields) field.Write(_allValues.GetValueOrDefault(name));
        _updating = false; UpdateVisibility(); ValuesChanged?.Invoke();
    }
    private static bool TryBool(object? value) { try { return ValueCodec.Boolean(value); } catch { return false; } }
}
