using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace ProjectLauncher.Core.Localization;

public static class TextCatalog
{
    // Presentation language only. Never changes numeric parsing or child process culture.
    public static string Language { get; set; } = "zh-CN";
    private static readonly IReadOnlyDictionary<string, string> English = Load("EnUs");
    private static readonly IReadOnlyDictionary<string, string> Chinese = Load("ZhCn");
    public static string Normalize(string? saved, string systemLanguage) => saved is "zh-CN" or "en-US"
        ? saved : systemLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh-CN" : "en-US";
    public static IEnumerable<string> Keys(string language) => Catalog(language).Keys;
    public static string Template(string language, string key) => Catalog(language).TryGetValue(key, out var value)
        ? value : English.TryGetValue(key, out value) ? value : key;
    public static string Format(string language, string key, params object?[] args) =>
        string.Format(CultureInfo.GetCultureInfo(language), Template(language, key), args);
    private static IReadOnlyDictionary<string, string> Catalog(string language) => language == "zh-CN" ? Chinese : English;
    private static IReadOnlyDictionary<string, string> Load(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"ProjectLauncher.Core.Localization.{name}.json")
            ?? throw new InvalidOperationException("Missing language resource: " + name);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? throw new InvalidOperationException("Empty language catalog");
    }
}
