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
        if (!Directory.Exists(directory)) throw new ConfigException("项目目录不存在：" + directory);
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
            if (!IsEnvironment(environment)) throw new ConfigException("所选目录不是完整的 Python 虚拟环境，需要 pyvenv.cfg 和环境内的 Python 解释器。\n" + environment);
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
                throw new ConfigException("请选择项目目录内已有的 .py 入口文件；也可以留空，稍后在项目设置中配置模块或自定义命令。");
            config.Actions[0].Argv = ["python", Path.GetRelativePath(project, script)];
        }
        return ConfigStore.Save(configPath, config, null);
    }
}
