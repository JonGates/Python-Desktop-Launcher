namespace ProjectLauncher.Core;

/// <summary>First-run discovery inspects files only; it never starts project Python or installs dependencies.</summary>
public static class ProjectSetup
{
    public static bool IsEnvironment(string directory) =>
        File.Exists(Path.Combine(directory, "pyvenv.cfg")) &&
        File.Exists(Path.Combine(directory, OperatingSystem.IsWindows() ? "Scripts" : "bin",
            OperatingSystem.IsWindows() ? "python.exe" : "python"));

    public static IReadOnlyList<string> FindEnvironments(string directory)
    {
        directory = Path.GetFullPath(directory);
        if (!Directory.Exists(directory)) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ProjectSetup.1", [directory]));
        var result = new List<string>();
        // One level only: never scan dependencies, recurse into repositories, or follow junctions.
        foreach (var child in new DirectoryInfo(directory).EnumerateDirectories("*", new EnumerationOptions
        {
            RecurseSubdirectories = false, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint
        }))
        {
            if (child.Name.Equals(".launcher", StringComparison.OrdinalIgnoreCase) ||
                child.Name.Equals(".git", StringComparison.OrdinalIgnoreCase)) continue;
            if (IsEnvironment(child.FullName)) result.Add(child.FullName);
        }
        return result.OrderBy(path => Path.GetFileName(path).ToLowerInvariant() switch
            { ".venv" => 0, "venv" => 1, "env" => 2, _ => 3 })
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static ConfigSnapshot Create(string configPath, string? environmentDirectory, string? entry)
    {
        configPath = Path.GetFullPath(configPath);
        // A configuration may have appeared while the setup dialog was open. Keep it untouched.
        if (File.Exists(configPath)) return ConfigStore.Load(configPath);
        var project = Path.GetDirectoryName(configPath)!;
        var config = ProjectInstaller.Discover(project);
        if (!string.IsNullOrWhiteSpace(environmentDirectory))
        {
            var environment = Path.GetFullPath(environmentDirectory, project);
            if (!IsEnvironment(environment)) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ProjectSetup.2", [environment]));
            config.Runtime.Mode = "existing";
            config.Runtime.Venv = ProjectEnvironment.IsInside(environment, project) ? Path.GetRelativePath(project, environment) : environment;
            config.Runtime.Requirements = "";
        }
        else
        {
            config.Runtime.Mode = File.Exists(Path.Combine(project, "uv.lock")) && File.Exists(Path.Combine(project, "pyproject.toml")) ? "uv" : "venv";
            config.Runtime.Venv = ".venv";
            config.Runtime.Requirements = config.Runtime.Mode == "venv" && File.Exists(Path.Combine(project, "requirements.txt")) ? "requirements.txt" : "";
        }
        if (string.IsNullOrWhiteSpace(entry))
        {
            config.Actions = [];
        }
        else
        {
            var script = Path.GetFullPath(entry, project);
            if (!File.Exists(script) || !ProjectEnvironment.IsInside(script, project) || !script.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
                throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ProjectSetup.3", []));
            config.Actions[0].Argv = ["python", Path.GetRelativePath(project, script)];
        }
        return ConfigStore.Save(configPath, config, null);
    }
}
