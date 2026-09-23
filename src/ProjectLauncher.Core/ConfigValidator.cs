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
        if (c.SchemaVersion != 1) throw new ConfigException("仅支持 schema_version: 1。");
        if (c.App is null || c.Runtime is null || c.Actions is null || c.Parameters is null) throw new ConfigException("app / runtime / actions / parameters 不能为 null。");
        NeedText(c.App.Name, "项目名称");
        c.App.Description ??= ""; c.App.Version ??= ""; c.App.OutputDir ??= "";
        if (c.Runtime.Mode is not ("uv" or "venv" or "existing")) throw new ConfigException("runtime.mode 必须是 uv、venv 或 existing。");
        NeedText(c.Runtime.ProjectDir, "项目目录"); NeedText(c.Runtime.Venv, "环境目录");
        c.Runtime.Python ??= ""; c.Runtime.Requirements ??= "";
        if (c.Runtime.Shell is not ("auto" or "powershell" or "pwsh" or "cmd")) throw new ConfigException("shell 必须是 auto / powershell / pwsh / cmd。");
        if (c.Runtime.Env is null) throw new ConfigException("runtime.env 必须是键值映射。");
        var environmentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in c.Runtime.Env)
        {
            CheckEnvName(key); CheckNul(value, "环境变量 " + key);
            if (!environmentKeys.Add(key)) throw new ConfigException("环境变量名不区分大小写，存在重复：" + key);
        }
        if (c.Actions.Count > 100) throw new ConfigException("最多定义 100 个启动动作。");
        if (c.Parameters.Count > 200) throw new ConfigException("最多定义 200 个参数。");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in c.Parameters)
        {
            if (p is null) throw new ConfigException("参数定义不能为 null。");
            CheckIdentifier(p.Name, "参数名");
            if (!names.Add(p.Name)) throw new ConfigException("参数名称重复：" + p.Name);
            p.Label ??= ""; p.Description ??= ""; p.Argument ??= ""; p.EnvName ??= ""; p.FalseArgument ??= "";
            if (!ParameterTypes.Contains(p.Type)) throw new ConfigException($"{p.Name} 的类型不受支持：{p.Type}");
            if (p.Binding is not ("argument" or "positional" or "env")) throw new ConfigException(p.DisplayName + " 的 binding 不合法。");
            if (p.Binding == "argument") NeedText(p.Argument, p.DisplayName + " 的命令行开关");
            if (p.Binding == "env") CheckEnvName(p.EnvName);
            if (p.Min is not null && !double.IsFinite(p.Min.Value) || p.Max is not null && !double.IsFinite(p.Max.Value)) throw new ConfigException("上下限必须为有限数值。");
            if (p.Min > p.Max) throw new ConfigException(p.DisplayName + " 的最小值不能大于最大值。");
            if (p.Options is null || p.VisibleWhen is null) throw new ConfigException(p.DisplayName + " 的 options / visible_when 不能为 null。");
            if (p.Type == "select" && (p.Options.Count == 0 || p.Options.Any(x => x is null))) throw new ConfigException(p.DisplayName + " 必须提供 options 字符串列表。");
            if (p.BooleanMode is not ("flag" or "value")) throw new ConfigException("boolean_mode 必须为 flag 或 value。");
            if (p.Type == "boolean" && p.Binding == "positional" && p.BooleanMode == "flag") throw new ConfigException("布尔位置参数需要 boolean_mode: value。");
            if (p.Default is not null && p.Default is not (string or bool or byte or short or int or long or uint or ulong or float or double or decimal)) throw new ConfigException(p.DisplayName + " 的 default 必须是标量值。");
            CheckNul(ValueCodec.Text(p.Default), p.DisplayName); CheckNul(p.Argument, p.Name); CheckNul(p.FalseArgument, p.Name);
        }
        foreach (var p in c.Parameters)
        {
            foreach (var (refName, value) in p.VisibleWhen)
            {
                if (!names.Contains(refName) || refName == p.Name) throw new ConfigException(p.DisplayName + " 的 visible_when 引用了不存在的参数或自身：" + refName);
                if (value is not null && value is not (string or bool or byte or short or int or long or uint or ulong or float or double or decimal)) throw new ConfigException("visible_when 的比较值必须是标量。");
            }
        }
        // Cycles are ambiguous for effective visibility. Reject rather than invent an evaluation order.
        foreach (var p in c.Parameters) CheckCycle(p.Name, c.Parameters.ToDictionary(x => x.Name), new HashSet<string>(), new HashSet<string>());
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var action in c.Actions)
        {
            if (action is null) throw new ConfigException("启动动作不能为 null。");
            CheckIdentifier(action.Id, "动作 ID");
            if (!ids.Add(action.Id)) throw new ConfigException("动作 ID 重复：" + action.Id);
            action.Label ??= action.Id;
            if (action.Argv is null || action.Argv.Count == 0 || action.Argv.Count > 500) throw new ConfigException(action.Label + " 必须提供非空 argv 数组（最多 500 项）。");
            NeedText(action.Argv[0], "启动程序");
            foreach (var arg in action.Argv) CheckNul(arg, "argv");
            if (action.Parameters is not null)
            {
                if (action.Parameters.Distinct().Count() != action.Parameters.Count) throw new ConfigException(action.Label + " 的参数引用重复。");
                foreach (var name in action.Parameters) if (!names.Contains(name)) throw new ConfigException(action.Label + " 引用了不存在的参数：" + name);
            }
            if (action.TimeoutSeconds is < 0 or > 604800) throw new ConfigException("超时秒数必须在 0 到 604800 之间，0 表示不限时。");
        }
    }

    private static void CheckCycle(string name, Dictionary<string, ParameterDefinition> map, HashSet<string> visiting, HashSet<string> done)
    {
        if (done.Contains(name)) return;
        if (!visiting.Add(name)) throw new ConfigException("visible_when 存在循环依赖：" + name);
        foreach (var key in map[name].VisibleWhen.Keys) CheckCycle(key, map, visiting, done);
        visiting.Remove(name); done.Add(name);
    }
    public static void CheckEnvName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !EnvironmentName.IsMatch(name)) throw new ConfigException("环境变量名称不合法：" + name);
        if (ReservedEnvironment.Contains(name)) throw new ConfigException("不能覆盖启动器管理的环境变量：" + name);
    }
    private static void CheckIdentifier(string value, string label) { if (value is null || !Identifier.IsMatch(value)) throw new ConfigException(label + " 应以英文字母或下划线开头，仅含英文、数字、下划线或短横线。"); }
    private static void NeedText(string? value, string label) { if (string.IsNullOrWhiteSpace(value)) throw new ConfigException(label + "不能为空。"); CheckNul(value, label); }
    public static void CheckNul(string? value, string label) { if (value is null || value.Contains('\0')) throw new ConfigException(label + "不能为 null 或包含 NUL 字符。"); }
}
