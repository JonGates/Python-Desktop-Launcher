using System.Collections;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProjectLauncher.Core;

public sealed class ProjectEnvironment
{
    public LauncherConfig Config { get; }
    public string ConfigPath { get; }
    public string Root { get; }
    public string EnvironmentDirectory { get; }
    public string PythonPath { get; }
    public string ScriptsDirectory { get; }
    public string StateDirectory { get; }
    public bool Exists => File.Exists(PythonPath) && File.Exists(Path.Combine(EnvironmentDirectory, "pyvenv.cfg"));
    public string? UvPath => FindExecutable("uv", CleanEnvironment());

    public ProjectEnvironment(LauncherConfig config, string configPath)
    {
        Config = config; ConfigPath = Path.GetFullPath(configPath);
        Root = Path.GetFullPath(config.Runtime.ProjectDir, Path.GetDirectoryName(ConfigPath)!);
        EnvironmentDirectory = Path.GetFullPath(config.Runtime.Venv, Root);
        ScriptsDirectory = Path.Combine(EnvironmentDirectory, OperatingSystem.IsWindows() ? "Scripts" : "bin");
        PythonPath = Path.Combine(ScriptsDirectory, OperatingSystem.IsWindows() ? "python.exe" : "python");
        StateDirectory = Path.Combine(Path.GetDirectoryName(ConfigPath)!, ".launcher", "csharp");
        var legacy = Path.Combine(Path.GetDirectoryName(ConfigPath)!, ".launcher", "runtime");
        var shellInternal = Path.Combine(Path.GetDirectoryName(ConfigPath)!, ".launcher");
        if (SamePath(EnvironmentDirectory, Root) || SamePath(EnvironmentDirectory, Path.GetPathRoot(EnvironmentDirectory)!) ||
            SamePath(EnvironmentDirectory, legacy) || SamePath(EnvironmentDirectory, shellInternal) || IsInside(EnvironmentDirectory, shellInternal))
            throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ProjectEnvironment.1", []));
    }

    public string RequirePython()
    {
        if (!Directory.Exists(Root)) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ProjectEnvironment.2", [Root]));
        if (!Exists) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ProjectEnvironment.3", [PythonPath]));
        return PythonPath;
    }

    public Dictionary<string, string> ExecutionEnvironment()
    {
        RequirePython();
        var env = CleanEnvironment();
        foreach (var (key, value) in Config.Runtime.Env) env[key] = value;
        env["PATH"] = ScriptsDirectory + Path.PathSeparator + env.GetValueOrDefault("PATH", "");
        env["VIRTUAL_ENV"] = EnvironmentDirectory;
        env["UV_PROJECT_ENVIRONMENT"] = EnvironmentDirectory;
        env["UV_PROJECT"] = Root;
        env["PYTHONNOUSERSITE"] = "1";
        env["PYTHONUTF8"] = "1"; env["PYTHONIOENCODING"] = "utf-8"; env["PYTHONUNBUFFERED"] = "1";
        return env;
    }

    public Dictionary<string, string> InitializationEnvironment()
    {
        var env = CleanEnvironment();
        foreach (var (key, value) in Config.Runtime.Env) env[key] = value;
        env["UV_PROJECT_ENVIRONMENT"] = EnvironmentDirectory;
        env["PYTHONUTF8"] = "1"; env["PYTHONIOENCODING"] = "utf-8"; env["PYTHONUNBUFFERED"] = "1";
        return env;
    }

    public List<CommandPlan> InitializationPlan()
    {
        if (!Directory.Exists(Root)) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ProjectEnvironment.4", [Root]));
        if (Config.Runtime.Mode == "existing") { RequirePython(); return []; }
        if (Directory.Exists(EnvironmentDirectory) && !Exists && Directory.EnumerateFileSystemEntries(EnvironmentDirectory).Any())
            throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ProjectEnvironment.5", [EnvironmentDirectory]));
        var plans = new List<CommandPlan>();
        var uv = UvPath;
        if (Config.Runtime.Mode == "uv")
        {
            if (uv is null) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ProjectEnvironment.6", []));
            if (!File.Exists(Path.Combine(Root, "pyproject.toml"))) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ProjectEnvironment.7", []));
            var args = new List<string> { uv, "sync", "--project", Root };
            if (File.Exists(Path.Combine(Root, "uv.lock"))) args.Add("--locked");
            if (!string.IsNullOrWhiteSpace(Config.Runtime.Python)) { args.Add("--python"); args.Add(Config.Runtime.Python); }
            plans.Add(Plan(args)); return plans;
        }
        if (!Exists)
        {
            if (uv is not null)
            {
                var args = new List<string> { uv, "venv", "--seed" };
                if (!string.IsNullOrWhiteSpace(Config.Runtime.Python)) { args.Add("--python"); args.Add(Config.Runtime.Python); }
                args.Add(EnvironmentDirectory); plans.Add(Plan(args));
            }
            else
            {
                var interpreter = InitializationPython();
                interpreter.AddRange(["-m", "venv", EnvironmentDirectory]); plans.Add(Plan(interpreter));
            }
        }
        if (!string.IsNullOrWhiteSpace(Config.Runtime.Requirements))
        {
            var requirements = Path.GetFullPath(Config.Runtime.Requirements, Root);
            if (!File.Exists(requirements)) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ProjectEnvironment.8", [requirements]));
            plans.Add(uv is not null ? Plan([uv, "pip", "install", "--python", PythonPath, "-r", requirements])
                : Plan([PythonPath, "-m", "pip", "install", "-r", requirements]));
        }
        return plans;
    }

    private List<string> InitializationPython()
    {
        var requested = Config.Runtime.Python.Trim(); var env = CleanEnvironment();
        if (requested.Length > 0)
        {
            if (Regex.IsMatch(requested, "^3\\.\\d+$"))
            {
                var py = OperatingSystem.IsWindows() ? FindExecutable("py", env) : null;
                if (py is not null) return [py, "-" + requested];
                throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ProjectEnvironment.9", []));
            }
            var path = CommandBuilder.IsExplicitPath(requested) ? Path.GetFullPath(requested, Root) : FindExecutable(requested, env);
            if (path is null || !File.Exists(path)) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ProjectEnvironment.10", [requested]));
            return [path];
        }
        if (OperatingSystem.IsWindows() && FindExecutable("py", env) is { } launcher) return [launcher, "-3"];
        var python = FindExecutable(OperatingSystem.IsWindows() ? "python" : "python3", env);
        if (python is null) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ProjectEnvironment.11", []));
        return [python];
    }

    public List<string> ShellCommand(string? choice = null)
    {
        var env = ExecutionEnvironment(); var shell = choice ?? Config.Runtime.Shell;
        if (shell == "python") return [RequirePython(), "-q"];
        if (!OperatingSystem.IsWindows()) return [FindExecutable("bash", env) ?? "/bin/sh"];
        if (shell is "auto" or "pwsh")
        {
            if (FindExecutable("pwsh", env) is { } pwsh) return [pwsh, "-NoLogo", "-NoProfile"];
            if (shell == "pwsh") throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ProjectEnvironment.12", []));
        }
        if (shell is "auto" or "powershell")
        {
            var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            if (File.Exists(powershell)) return [powershell, "-NoLogo", "-NoProfile"];
        }
        return [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"), "/d"];
    }

    public async Task<EnvironmentProbe> ProbeAsync(CancellationToken cancellation = default)
    {
        var python = RequirePython();
        const string script = "import sys,json,importlib.util; print(json.dumps({'executable':sys.executable,'prefix':sys.prefix,'base_prefix':sys.base_prefix,'version':sys.version.split()[0],'has_pip':importlib.util.find_spec('pip') is not None}))";
        using var runner = new ProcessRunner();
        var output = new System.Text.StringBuilder(); var gate = new object();
        runner.Output += text => { lock (gate) output.Append(text); };
        var result = await runner.RunAsync(new([python, "-I", "-c", script], new(), ProjectLauncher.Core.Localization.TextCatalog.Format(ProjectLauncher.Core.Localization.TextCatalog.Language, "Runtime.3"), [], 20), Root, ExecutionEnvironment(), cancellation);
        if (result.ExitCode != 0 || result.Cancelled || result.TimedOut) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ProjectEnvironment.13", [output]));
        var json = output.ToString().Split('\n').LastOrDefault(line => line.TrimStart().StartsWith('{'));
        if (json is null) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ProjectEnvironment.14", []));
        using var doc = JsonDocument.Parse(json);
        var d = doc.RootElement;
        var probe = new EnvironmentProbe(d.GetProperty("executable").GetString()!, d.GetProperty("prefix").GetString()!,
            d.GetProperty("base_prefix").GetString()!, d.GetProperty("version").GetString()!, d.GetProperty("has_pip").GetBoolean());
        if (!SamePath(probe.Prefix, EnvironmentDirectory) || SamePath(probe.Prefix, probe.BasePrefix))
            throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ProjectEnvironment.15", [probe.Prefix]));
        return probe;
    }

    public static Dictionary<string, string> CleanEnvironment()
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            if (entry.Key is string key && entry.Value is string value) env[key] = value;
        var staleRoots = new[] { env.GetValueOrDefault("VIRTUAL_ENV"), env.GetValueOrDefault("CONDA_PREFIX") }.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        var path = env.GetValueOrDefault("PATH", "");
        var parts = path.Split(Path.PathSeparator).Where(p => !string.IsNullOrWhiteSpace(p) &&
            !staleRoots.Any(root => SamePath(p.Trim('"'), root!) || IsInside(p.Trim('"'), root!))).ToList();
        foreach (var name in ConfigValidator.ReservedEnvironment.Where(name => name != "PATH" && name != "PATHEXT" && name != "COMSPEC")) env.Remove(name);
        env["PATH"] = string.Join(Path.PathSeparator, parts);
        return env;
    }

    /// <summary>Windows executable lookup must use the child's project PATH, not the launcher's inherited PATH.</summary>
    public static string ResolveExecutable(string command, string cwd, IReadOnlyDictionary<string, string> environment)
    {
        var path = CommandBuilder.IsExplicitPath(command) ? Path.GetFullPath(command, cwd) : FindExecutable(command, environment);
        if (path is null || !File.Exists(path)) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ProjectEnvironment.16", [command]));
        if (OperatingSystem.IsWindows() && Path.GetExtension(path).ToLowerInvariant() is ".bat" or ".cmd")
            throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ProjectEnvironment.17", []));
        return path;
    }

    public static string? FindExecutable(string name, IReadOnlyDictionary<string, string> env)
    {
        if (CommandBuilder.IsExplicitPath(name)) return File.Exists(name) ? Path.GetFullPath(name) : null;
        var extensions = OperatingSystem.IsWindows() && !Path.HasExtension(name) ? new[] { ".exe", ".com" } : new[] { "" };
        foreach (var directory in env.GetValueOrDefault("PATH", "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;
            foreach (var extension in extensions)
            {
                try
                {
                    var candidate = Path.Combine(directory.Trim('"'), name + extension);
                    // Windows Store Python aliases may start the Store instead of an interpreter.
                    if (name.StartsWith("python", StringComparison.OrdinalIgnoreCase) && candidate.Contains("Microsoft\\WindowsApps", StringComparison.OrdinalIgnoreCase)) continue;
                    if (File.Exists(candidate)) return Path.GetFullPath(candidate);
                }
                catch (Exception e) when (e is ArgumentException or NotSupportedException) { }
            }
        }
        return null;
    }

    public static bool SamePath(string a, string b)
    {
        try { return string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal); }
        catch { return false; }
    }
    public static bool IsInside(string path, string parent)
    {
        try { return Path.GetFullPath(path).StartsWith(Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal); }
        catch { return false; }
    }
    private static CommandPlan Plan(List<string> args) => new(args, new(), WindowsArguments.Join(args), []);
}

public sealed record EnvironmentProbe(string Executable, string Prefix, string BasePrefix, string Version, bool HasPip);
