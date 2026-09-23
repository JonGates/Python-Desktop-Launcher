using System.Globalization;
using System.Text.Json;

namespace ProjectLauncher.Core;

public sealed class ConfigException : Exception
{
    public ConfigException(string message) : base(message) { }
    public ConfigException(string message, Exception inner) : base(message, inner) { }
}

public sealed class LauncherConfig
{
    public int SchemaVersion { get; set; } = 1;
    public AppDefinition App { get; set; } = new();
    public RuntimeDefinition Runtime { get; set; } = new();
    public List<ActionDefinition> Actions { get; set; } = [];
    public List<ParameterDefinition> Parameters { get; set; } = [];
}

public sealed class AppDefinition
{
    public string Name { get; set; } = "我的 Python 项目";
    public string Description { get; set; } = "配置参数，在项目环境中运行。";
    public string Version { get; set; } = "1.0.0";
    public string OutputDir { get; set; } = "output";
}

public sealed class RuntimeDefinition
{
    public string Mode { get; set; } = "venv";
    public string ProjectDir { get; set; } = ".";
    public string Venv { get; set; } = ".venv";
    public string Python { get; set; } = "";
    public string Requirements { get; set; } = "";
    public string Shell { get; set; } = "auto";
    public Dictionary<string, string> Env { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ActionDefinition
{
    public string Id { get; set; } = "run";
    public string Label { get; set; } = "运行项目";
    public List<string> Argv { get; set; } = ["python", "main.py"];
    // null = all parameters; [] = no parameters. Do not collapse these states.
    public List<string>? Parameters { get; set; }
    public int TimeoutSeconds { get; set; }
    public override string ToString() => string.IsNullOrWhiteSpace(Label) ? Id : Label;
}

public sealed class ParameterDefinition
{
    public string Name { get; set; } = "parameter";
    public string Label { get; set; } = "";
    public string Description { get; set; } = "";
    public string Type { get; set; } = "text";
    public object? Default { get; set; }
    public bool Required { get; set; }
    public bool Advanced { get; set; }
    public string Binding { get; set; } = "argument";
    public string Argument { get; set; } = "";
    public string EnvName { get; set; } = "";
    public double? Min { get; set; }
    public double? Max { get; set; }
    public List<string> Options { get; set; } = [];
    public bool MustExist { get; set; }
    public bool Secret { get; set; }
    public string BooleanMode { get; set; } = "flag";
    public string FalseArgument { get; set; } = "";
    public Dictionary<string, object?> VisibleWhen { get; set; } = new(StringComparer.Ordinal);
    [YamlDotNet.Serialization.YamlIgnore]
    public bool IsSecret => Secret || Type == "password";
    [YamlDotNet.Serialization.YamlIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Label) ? Name : Label;
    public override string ToString() => DisplayName;
}

public static class ValueCodec
{
    public static object? Unwrap(object? value) => value is JsonElement j ? j.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => j.TryGetInt64(out var n) ? (object)n : j.GetDouble(),
        JsonValueKind.String => j.GetString(),
        _ => j.GetRawText()
    } : value;

    public static string Text(object? value)
    {
        value = Unwrap(value);
        return value switch
        {
            null => "", bool b => b ? "true" : "false",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? ""
        };
    }

    public static bool Boolean(object? value)
    {
        value = Unwrap(value);
        if (value is bool b) return b;
        var s = Text(value).Trim();
        if (s.Length == 0) return false;
        if (bool.TryParse(s, out b)) return b;
        throw new ConfigException($"布尔值必须为 true 或 false，当前值为：{s}");
    }

    public static bool Equivalent(object? left, object? right) =>
        string.Equals(Text(left), Text(right), StringComparison.Ordinal);
}

public sealed record CommandPlan(List<string> Argv, Dictionary<string, string> Environment,
    string Preview, List<string> Secrets, int TimeoutSeconds = 0);
public sealed record ConfigSnapshot(string Path, LauncherConfig Config, string Hash);
public sealed record ProcessResult(int ExitCode, bool Cancelled, bool TimedOut, TimeSpan Duration);
