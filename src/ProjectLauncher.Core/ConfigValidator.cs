using System.Text.RegularExpressions;

namespace ProjectLauncher.Core;

public static class ConfigValidator
{
    public static readonly string[] ParameterTypes = ["text", "integer", "number", "boolean", "select", "file", "directory", "password", "textarea"];
    public static readonly HashSet<string> ReservedEnvironment = new(StringComparer.OrdinalIgnoreCase)
    {
        "PATH", "PATHEXT", "VIRTUAL_ENV", "VIRTUAL_ENV_PROMPT", "PYTHONHOME", "PYTHONPATH", "PYTHONSTARTUP",
        "PYTHONUSERBASE", "PYTHONNOUSERSITE", "PYTHONUTF8", "PYTHONIOENCODING", "PYTHONUNBUFFERED",
        "UV_PROJECT", "UV_PROJECT_ENVIRONMENT", "UV_PYTHON", "CONDA_PREFIX", "CONDA_DEFAULT_ENV", "COMSPEC"
    };
    private static readonly Regex Identifier = new("^[A-Za-z_][A-Za-z0-9_-]*$", RegexOptions.CultureInvariant);
    private static readonly Regex EnvironmentName = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

    public static void Validate(LauncherConfig c)
    {
        if (c.SchemaVersion != 1) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigValidator.1", []));
        if (c.App is null || c.Runtime is null || c.Actions is null || c.Parameters is null) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigValidator.2", []));
        NeedText(c.App.Name, "app.name");
        c.App.Description ??= ""; c.App.Version ??= ""; c.App.OutputDir ??= "";
        if (c.Runtime.Mode is not ("uv" or "venv" or "existing")) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigValidator.3", []));
        NeedText(c.Runtime.ProjectDir, "runtime.project_dir"); NeedText(c.Runtime.Venv, "runtime.venv");
        c.Runtime.Python ??= ""; c.Runtime.Requirements ??= "";
        if (c.Runtime.Shell is not ("auto" or "powershell" or "pwsh" or "cmd")) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigValidator.4", []));
        if (c.Runtime.Env is null) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigValidator.5", []));
        var environmentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in c.Runtime.Env)
        {
            CheckEnvName(key); CheckNul(value, "runtime.env: " + key);
            if (!environmentKeys.Add(key)) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigValidator.6", [key]));
        }
        if (c.Actions.Count > 100) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigValidator.7", []));
        if (c.Parameters.Count > 200) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigValidator.8", []));
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in c.Parameters)
        {
            if (p is null) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigValidator.9", []));
            CheckIdentifier(p.Name, "parameter.name");
            if (!names.Add(p.Name)) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigValidator.10", [p.Name]));
            p.Label ??= ""; p.Description ??= ""; p.Argument ??= ""; p.EnvName ??= ""; p.FalseArgument ??= "";
            if (!ParameterTypes.Contains(p.Type)) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigValidator.11", [p.Name, p.Type]));
            if (p.Binding is not ("argument" or "positional" or "env")) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigValidator.12", [p.DisplayName]));
            if (p.Binding == "argument") NeedText(p.Argument, p.DisplayName + " argument");
            if (p.Binding == "env") CheckEnvName(p.EnvName);
            if (p.Min is not null && !double.IsFinite(p.Min.Value) || p.Max is not null && !double.IsFinite(p.Max.Value)) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigValidator.13", []));
            if (p.Min > p.Max) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigValidator.14", [p.DisplayName]));
            if (p.Options is null || p.VisibleWhen is null) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigValidator.15", [p.DisplayName]));
            if (p.Type == "select" && (p.Options.Count == 0 || p.Options.Any(x => x is null))) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigValidator.16", [p.DisplayName]));
            if (p.BooleanMode is not ("flag" or "value")) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigValidator.17", []));
            if (p.Type == "boolean" && p.Binding == "positional" && p.BooleanMode == "flag") throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigValidator.18", []));
            if (p.Default is not null && p.Default is not (string or bool or byte or short or int or long or uint or ulong or float or double or decimal)) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigValidator.19", [p.DisplayName]));
            CheckNul(ValueCodec.Text(p.Default), p.DisplayName); CheckNul(p.Argument, p.Name); CheckNul(p.FalseArgument, p.Name);
        }
        foreach (var p in c.Parameters)
        {
            foreach (var (refName, value) in p.VisibleWhen)
            {
                if (!names.Contains(refName) || refName == p.Name) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigValidator.20", [p.DisplayName, refName]));
                if (value is not null && value is not (string or bool or byte or short or int or long or uint or ulong or float or double or decimal)) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigValidator.21", []));
            }
        }
        // Cycles are ambiguous for effective visibility. Reject rather than invent an evaluation order.
        foreach (var p in c.Parameters) CheckCycle(p.Name, c.Parameters.ToDictionary(x => x.Name), new HashSet<string>(), new HashSet<string>());
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var action in c.Actions)
        {
            if (action is null) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigValidator.22", []));
            CheckIdentifier(action.Id, "action.id");
            if (!ids.Add(action.Id)) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigValidator.23", [action.Id]));
            action.Label ??= action.Id;
            if (action.Argv is null || action.Argv.Count == 0 || action.Argv.Count > 500) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigValidator.24", [action.Label]));
            NeedText(action.Argv[0], "argv[0]");
            foreach (var arg in action.Argv) CheckNul(arg, "argv");
            if (action.Parameters is not null)
            {
                if (action.Parameters.Distinct().Count() != action.Parameters.Count) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigValidator.25", [action.Label]));
                foreach (var name in action.Parameters) if (!names.Contains(name)) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigValidator.26", [action.Label, name]));
            }
            if (action.TimeoutSeconds is < 0 or > 604800) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigValidator.27", []));
        }
    }

    private static void CheckCycle(string name, Dictionary<string, ParameterDefinition> map, HashSet<string> visiting, HashSet<string> done)
    {
        if (done.Contains(name)) return;
        if (!visiting.Add(name)) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigValidator.28", [name]));
        foreach (var key in map[name].VisibleWhen.Keys) CheckCycle(key, map, visiting, done);
        visiting.Remove(name); done.Add(name);
    }
    public static void CheckEnvName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !EnvironmentName.IsMatch(name)) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigValidator.29", [name]));
        if (ReservedEnvironment.Contains(name)) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigValidator.30", [name]));
    }
    private static void CheckIdentifier(string value, string label) { if (value is null || !Identifier.IsMatch(value)) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigValidator.31", [label])); }
    private static void NeedText(string? value, string label) { if (string.IsNullOrWhiteSpace(value)) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigValidator.32", [label])); CheckNul(value, label); }
    public static void CheckNul(string? value, string label) { if (value is null || value.Contains('\0')) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigValidator.33", [label])); }
}
