namespace ProjectLauncher.Core.Localization;

public sealed record LocalizedDiagnostic(string Key, object?[] Arguments, string? TechnicalDetail = null)
{
    public string Render(string language)
    {
        var text = TextCatalog.Format(language, Key, Arguments.Select(a => a is LocalizedDiagnostic nested ? nested.Render(language) : a).ToArray());
        return string.IsNullOrEmpty(TechnicalDetail) ? text : text + "\n" + TechnicalDetail;
    }
}
