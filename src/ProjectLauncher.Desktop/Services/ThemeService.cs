using System.Windows;

namespace ProjectLauncher.Desktop.Services;

public static class ThemeService
{
    public static string Current { get; private set; } = "dark";
    public static void Apply(string theme)
    {
        Current = theme == "light" ? "light" : "dark";
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var old = dictionaries.FirstOrDefault(d => d.Source is not null && (d.Source.OriginalString.EndsWith("Dark.xaml") || d.Source.OriginalString.EndsWith("Light.xaml")));
        var dictionary = new ResourceDictionary { Source = new Uri($"Themes/{(Current == "light" ? "Light" : "Dark")}.xaml", UriKind.Relative) };
        if (old is not null) dictionaries[dictionaries.IndexOf(old)] = dictionary; else dictionaries.Insert(0, dictionary);
    }
}
