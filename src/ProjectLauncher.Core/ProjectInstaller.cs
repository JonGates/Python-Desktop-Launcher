namespace ProjectLauncher.Core;

public static class ProjectInstaller
{
    public static LauncherConfig Discover(string directory, string? entry = null)
    {
        directory = Path.GetFullPath(directory);
        if (!Directory.Exists(directory)) throw new ConfigException("目标项目目录不存在。");
        entry ??= new[] { "main.py", "app.py", "run.py", "server.py" }.FirstOrDefault(f => File.Exists(Path.Combine(directory, f))) ?? "main.py";
        var environment = ProjectSetup.FindEnvironments(directory).FirstOrDefault();
        var mode = environment is not null ? "existing" :
            File.Exists(Path.Combine(directory, "uv.lock")) && File.Exists(Path.Combine(directory, "pyproject.toml")) ? "uv" : "venv";
        return new LauncherConfig
        {
            App = new() { Name = new DirectoryInfo(directory).Name, Description = "在项目自己的 Python 环境中运行。请在项目设置中核对真实入口和参数。" },
            Runtime = new() { Mode = mode, Venv = environment is null ? ".venv" : Path.GetRelativePath(directory, environment),
                Requirements = mode == "venv" && File.Exists(Path.Combine(directory, "requirements.txt")) ? "requirements.txt" : "" },
            Actions = [new() { Id = "run", Label = "运行项目", Argv = ["python", entry], Parameters = [] }], Parameters = []
        };
    }

    public static List<string> Install(string launcherExecutable, string directory, string entry)
    {
        directory = Path.GetFullPath(directory);
        if (!Directory.Exists(directory)) throw new ConfigException("请选择已经存在的项目目录。");
        if (!File.Exists(launcherExecutable)) throw new ConfigException("找不到可复制的启动器文件。");
        var target = Path.Combine(directory, "Launcher.exe");
        var config = Path.Combine(directory, "launcher.yaml");
        if (File.Exists(target) || Directory.Exists(target)) throw new ConfigException("目标目录已有 Launcher.exe。为避免覆盖，接入已取消。升级时请先备份并重命名旧 EXE。");
        if (File.Exists(config)) ConfigStore.Load(config); // Validate before any copy; existing configuration is kept verbatim.
        var created = new List<string>();
        var temporary = Path.Combine(directory, ".launcher-copy-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.Copy(launcherExecutable, temporary, false);
            File.Move(temporary, target, overwrite: false); created.Add(target);
            if (!File.Exists(config))
            {
                var text = ConfigStore.Serialize(Discover(directory, entry));
                using var stream = new FileStream(config, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                created.Add(config);
                using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)); writer.Write(text);
            }
            return created;
        }
        catch
        {
            // Roll back only the files created during this call, never a pre-existing target file.
            foreach (var path in created.AsEnumerable().Reverse()) try { File.Delete(path); } catch { }
            throw;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
