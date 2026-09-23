using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Controls;
using System.Globalization;
using ProjectLauncher.Core.Localization;

namespace ProjectLauncher.Desktop.Services;

[MarkupExtensionReturnType(typeof(object))]
public sealed class LocalizedText(string key) : MarkupExtension
{
    public string Key { get; set; } = key;
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = LocalizationService.Current, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}

public static class L
{
    public static void Options(ComboBox box, IReadOnlyDictionary<string, string> keys)
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        var binding = new MultiBinding { Converter = new OptionConverter(keys) };
        binding.Bindings.Add(new Binding("."));
        binding.Bindings.Add(new Binding(nameof(LocalizationService.Language)) { Source = LocalizationService.Current });
        text.SetBinding(TextBlock.TextProperty, binding);
        box.ItemTemplate = new DataTemplate { VisualTree = text };
    }
    private sealed class OptionConverter(IReadOnlyDictionary<string, string> keys) : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var value = values[0]?.ToString() ?? "";
            return keys.TryGetValue(value, out var key) ? Text(key) : value;
        }
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
    public static string Text(string key, params object?[] args) => TextCatalog.Format(LocalizationService.Current.Language, key, args);
    public static T Localize<T>(this T element, DependencyProperty property, string key, params object?[] args) where T : FrameworkElement
    {
        return Dynamic(element, property, () => Text(key, args));
    }
    public static T Dynamic<T>(this T element, DependencyProperty property, Func<string> render) where T : FrameworkElement
    {
        element.SetBinding(property, new Binding(nameof(LocalizedValue.Text)) { Source = new LocalizedValue(render), Mode = BindingMode.OneWay });
        return element;
    }
    private sealed class LocalizedValue : INotifyPropertyChanged
    {
        private readonly Func<string> _render;
        public LocalizedValue(Func<string> render)
        {
            _render = render;
            PropertyChangedEventManager.AddHandler(LocalizationService.Current, Changed, nameof(LocalizationService.Language));
        }
        public string Text => _render();
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Changed(object? sender, PropertyChangedEventArgs e) => PropertyChanged?.Invoke(this, new(nameof(Text)));
    }
}
