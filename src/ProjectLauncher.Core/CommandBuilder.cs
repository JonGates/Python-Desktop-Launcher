using System.Globalization;

namespace ProjectLauncher.Core;

public static class CommandBuilder
{
    public static Dictionary<string, object?> EffectiveValues(LauncherConfig config, IReadOnlyDictionary<string, object?> supplied)
    {
        var result = config.Parameters.ToDictionary(p => p.Name, p => p.Default, StringComparer.Ordinal);
        foreach (var (key, value) in supplied) if (result.ContainsKey(key)) result[key] = ValueCodec.Unwrap(value);
        return result;
    }

    public static bool IsVisible(ParameterDefinition p, IReadOnlyDictionary<string, object?> values) =>
        p.VisibleWhen.All(pair => values.TryGetValue(pair.Key, out var actual) && ValueCodec.Equivalent(actual, pair.Value));

    public static IReadOnlyList<ParameterDefinition> SelectedParameters(LauncherConfig c, ActionDefinition action)
    {
        if (action.Parameters is null) return c.Parameters;
        var map = c.Parameters.ToDictionary(p => p.Name, StringComparer.Ordinal);
        return action.Parameters.Select(n => map[n]).ToList();
    }

    public static CommandPlan Build(LauncherConfig config, string actionId, Dictionary<string, object?> values,
        string projectRoot, string projectPython)
    {
        ConfigValidator.Validate(config);
        var action = config.Actions.FirstOrDefault(a => a.Id == actionId) ?? throw new ConfigException(new Localization.LocalizedDiagnostic("Error.CommandBuilder.1", [actionId]));
        var effective = EffectiveValues(config, values);
        var argv = action.Argv.ToList();
        var first = Path.GetFileName(argv[0]).ToLowerInvariant();
        // Only bare interpreter tokens are replaced. An explicitly selected absolute executable is a trusted config choice.
        if (new[] { "python", "python.exe", "python3", "python3.exe", "pythonw", "pythonw.exe" }.Contains(first) && !IsExplicitPath(argv[0])) argv[0] = projectPython;
        else if (new[] { "pip", "pip.exe", "pip3", "pip3.exe" }.Contains(first) && !IsExplicitPath(argv[0]))
        { argv.RemoveAt(0); argv.InsertRange(0, [projectPython, "-m", "pip"]); }
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var secrets = new List<string>();
        foreach (var p in SelectedParameters(config, action))
        {
            if (!IsVisible(p, effective)) continue;
            var text = Normalize(p, effective[p.Name], projectRoot);
            if (text is null) continue;
            if (p.IsSecret && text.Length > 0) secrets.Add(text);
            if (p.Binding == "env") { environment[p.EnvName] = text; continue; }
            if (p.Type == "boolean" && p.BooleanMode == "flag")
            {
                if (text == "true") argv.Add(p.Argument);
                else if (p.FalseArgument.Length > 0) argv.Add(p.FalseArgument);
                continue;
            }
            if (p.Binding == "argument") argv.Add(p.Argument);
            argv.Add(text);
        }
        string preview = WindowsArguments.Join(argv);
        foreach (var secret in secrets.OrderByDescending(x => x.Length)) preview = preview.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
        return new(argv, environment, preview, secrets, action.TimeoutSeconds);
    }

    public static bool IsExplicitPath(string command) => command.Contains('/') || command.Contains('\\') || Path.IsPathRooted(command);

    private static string? Normalize(ParameterDefinition p, object? value, string root)
    {
        var text = ValueCodec.Text(value);
        if (text.Contains('\0')) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.CommandBuilder.2", [p.DisplayName]));
        if (text.Length > 65536 || p.IsSecret && text.Length > 8192) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.CommandBuilder.3", [p.DisplayName]));
        if (p.Type == "boolean")
        {
            try { return ValueCodec.Boolean(value) ? "true" : "false"; }
            catch (ConfigException) { throw new ConfigException(new Localization.LocalizedDiagnostic("Error.CommandBuilder.4", [p.DisplayName])); }
        }
        if (string.IsNullOrWhiteSpace(text))
        {
            if (p.Required) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.CommandBuilder.5", [p.DisplayName]));
            return null;
        }
        if (p.Type == "integer")
        {
            if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.CommandBuilder.6", [p.DisplayName]));
            CheckRange(p, integer); return integer.ToString(CultureInfo.InvariantCulture);
        }
        if (p.Type == "number")
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number)) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.CommandBuilder.7", [p.DisplayName]));
            CheckRange(p, number); return number.ToString("G17", CultureInfo.InvariantCulture);
        }
        if (p.Type == "select" && !p.Options.Contains(text, StringComparer.Ordinal)) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.CommandBuilder.8", [p.DisplayName]));
        if (p.Type is "file" or "directory")
        {
            text = Path.GetFullPath(text, root);
            if (p.MustExist && !(p.Type == "file" ? File.Exists(text) : Directory.Exists(text))) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.CommandBuilder.9", [p.DisplayName, text]));
        }
        return text;
    }
    private static void CheckRange(ParameterDefinition p, double value)
    {
        if (p.Min is not null && value < p.Min || p.Max is not null && value > p.Max) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.CommandBuilder.10", [p.DisplayName, p.Min?.ToString(CultureInfo.InvariantCulture) ?? "unlimited", p.Max?.ToString(CultureInfo.InvariantCulture) ?? "unlimited"]));
    }
}

public static class WindowsArguments
{
    public static string Join(IEnumerable<string> args) => string.Join(" ", args.Select(Quote));
    // Windows CommandLineToArgvW / CRT quoting. Used only at the native ConPTY boundary and for display.
    public static string Quote(string value)
    {
        if (value.Length > 0 && !value.Any(c => char.IsWhiteSpace(c) || c == '"')) return value;
        var b = new System.Text.StringBuilder("\""); int slash = 0;
        foreach (var c in value)
        {
            if (c == '\\') { slash++; continue; }
            if (c == '"') b.Append('\\', slash * 2 + 1).Append('"');
            else b.Append('\\', slash).Append(c);
            slash = 0;
        }
        return b.Append('\\', slash * 2).Append('"').ToString();
    }
}
