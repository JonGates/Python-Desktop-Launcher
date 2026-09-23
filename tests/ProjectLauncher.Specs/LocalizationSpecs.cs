using ProjectLauncher.Core.Localization;

sealed partial class SpecSuite
{
    private void LocalizationSpecs()
    {
        Test("language prefers saved selection and maps system culture", () => {
            Eq("zh-CN", TextCatalog.Normalize(null, "zh-TW"));
            Eq("en-US", TextCatalog.Normalize(null, "fr-FR"));
            Eq("en-US", TextCatalog.Normalize("en-US", "zh-CN"));
            Eq("zh-CN", TextCatalog.Normalize("invalid", "zh-CN"));
        });
        Test("catalog parity formatting and user text preservation", () => {
            True(TextCatalog.Keys("zh-CN").Order().SequenceEqual(TextCatalog.Keys("en-US").Order()));
            Eq("Invalid number: 保存", TextCatalog.Format("en-US", "Error.InvalidNumber", "保存"));
            Eq("Common.Unknown", TextCatalog.Format("en-US", "Common.Unknown"));
            foreach (var key in TextCatalog.Keys("en-US"))
                Eq(System.Text.CompositeFormat.Parse(TextCatalog.Template("zh-CN", key)).MinimumArgumentCount,
                   System.Text.CompositeFormat.Parse(TextCatalog.Template("en-US", key)).MinimumArgumentCount);
        });
        Test("language preferences round trip independently of project", () => {
            var path = Path.Combine(_root, "preferences", "preferences.json");
            var prefs = new LanguagePreferences(path);
            Eq<string?>(null, prefs.Load().Language);
            Eq<string?>(null, prefs.Save("en-US"));
            Eq("en-US", prefs.Load().Language);
            True(!File.Exists(Path.Combine(_root, "preferences", "launcher.yaml")));
        });
        Test("corrupt preference is preserved and writable recovery is atomic", () => {
            var path = Path.Combine(_root, "corrupt-language.json");
            File.WriteAllText(path, "broken JSON");
            var prefs = new LanguagePreferences(path);
            True(prefs.Load().Error is not null);
            Eq("broken JSON", File.ReadAllText(path));
            Eq<string?>(null, prefs.Save("zh-CN"));
            Eq("zh-CN", prefs.Load().Language);
            True(Directory.GetFiles(_root, "corrupt-language.json.corrupt-*").Any(p => File.ReadAllText(p) == "broken JSON"));
        });
        Test("preference write failure is returned without destroying other data", () => {
            var blocker = Path.Combine(_root, "language-blocker"); File.WriteAllText(blocker, "keep");
            True(new LanguagePreferences(Path.Combine(blocker, "preferences.json")).Save("en-US") is not null);
            Eq("keep", File.ReadAllText(blocker));
        });
    }
}
