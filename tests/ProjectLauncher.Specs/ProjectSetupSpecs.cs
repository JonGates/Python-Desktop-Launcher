using ProjectLauncher.Core;

sealed partial class SpecSuite
{
    private string SetupProject()
    {
        var path = Path.Combine(_root, "首次 接入 " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path); return path;
    }
    private static string StubEnvironment(string project, string name)
    {
        var path = Path.Combine(project, name);
        var scripts = Path.Combine(path, OperatingSystem.IsWindows() ? "Scripts" : "bin");
        Directory.CreateDirectory(scripts);
        File.WriteAllText(Path.Combine(path, "pyvenv.cfg"), "home = test");
        File.WriteAllText(Path.Combine(scripts, OperatingSystem.IsWindows() ? "python.exe" : "python"), "fixture; never execute");
        return path;
    }
    private void RunSetupSpecs()
    {
        Test("parameter dialog copies shared definitions and dependent conditions only for selected action", () => {
            var c = new LauncherConfig { Parameters = [new() { Name = "port", Argument = "--port", Default = 8000L }, new() { Name = "dependent", Argument = "--dependent", VisibleWhen = new() { ["port"] = 8000L } }], Actions = [new() { Id = "one", Parameters = ["port", "dependent"] }, new() { Id = "two", Parameters = ["port", "dependent"] }] };
            var edited = ActionEditing.CloneParameter(c.Parameters[0]); edited.Default = 9000L;
            Eq("8000", ValueCodec.Text(c.Parameters[0].Default));
            ActionEditing.SaveParameter(c, c.Actions[0], "port", edited, false);
            Eq("8000", ValueCodec.Text(c.Parameters[0].Default));
            True(c.Actions[1].Parameters!.SequenceEqual(new[] { "port", "dependent" }));
            var localPort = c.Actions[0].Parameters![0];
            var localDependent = c.Parameters.Single(p => p.Name == c.Actions[0].Parameters![1]);
            True(localPort != "port" && localDependent.Name != "dependent");
            True(localDependent.VisibleWhen.ContainsKey(localPort)); ConfigValidator.Validate(c);
        });
        Test("parameter dialog invalid save is atomic and requires confirmation for implicit memberships", () => {
            var c = new LauncherConfig { Actions = [new() { Id = "one" }, new() { Id = "two" }] };
            var before = ConfigStore.Serialize(c);
            Throws(() => ActionEditing.SaveParameter(c, c.Actions[0], null, new() { Argument = "--test" }, false));
            Eq(before, ConfigStore.Serialize(c));
            Throws(() => ActionEditing.SaveParameter(c, c.Actions[0], null, new() { Binding = "env", EnvName = "PATH" }, true));
            Eq(before, ConfigStore.Serialize(c));
            ActionEditing.SaveParameter(c, c.Actions[0], null, new() { Argument = "--test" }, true);
            Eq(1, c.Actions[0].Parameters!.Count); Eq(0, c.Actions[1].Parameters!.Count);
        });
        Test("browsed program remains an explicit project path instead of PATH lookup", () => {
            var project = SetupProject(); var executable = Path.Combine(project, "custom.exe"); File.WriteAllText(executable, "fixture");
            var selected = ActionEditing.BrowsedTarget(project, executable);
            Eq(executable, ProjectEnvironment.ResolveExecutable(selected, project, new Dictionary<string, string> { ["PATH"] = "" }));
        });
        Test("action editor assigns stable identifiers and binds new parameters only to selected action", () => {
            var c = Simple(); var existing = c.Actions[0]; existing.Parameters = null;
            var a = ActionEditing.AddAction(c); a.Label = "启动服务"; var id = a.Id;
            Throws(() => ActionEditing.AddParameter(c, a));
            var p = ActionEditing.AddParameter(c, a, true); p.Label = "端口"; p.Argument = "--port";
            Eq(id, a.Id); True(a.Parameters!.SequenceEqual(new[] { p.Name }));
            True(existing.Parameters is not null && !existing.Parameters.Contains(p.Name));
            ConfigValidator.Validate(c);
        });
        Test("action editor distinguishes arbitrary commands from script and module targets", () => {
            Eq("script", ActionEditing.CommandKind(new() { Argv = ["python", "my app.py"] }));
            Eq("module", ActionEditing.CommandKind(new() { Argv = ["python", "-m", "uvicorn"] }));
            Eq("advanced", ActionEditing.CommandKind(new() { Argv = ["python", "-I", "-c", "print(1)"] }));
        });
        Test("setup discovers multiple environments without entering internal directories", () => {
            var p = SetupProject(); StubEnvironment(p, "z env"); StubEnvironment(p, "venv"); StubEnvironment(p, ".venv");
            StubEnvironment(p, Path.Combine(".launcher", "runtime")); StubEnvironment(p, Path.Combine("nested", "hidden"));
            var found = ProjectSetup.FindEnvironments(p);
            True(found.Select(Path.GetFileName).SequenceEqual(new[] { ".venv", "venv", "z env" }));
        });
        Test("setup binds selected relative environment and spaced entry without executing", () => {
            var p = SetupProject(); var v = StubEnvironment(p, "my env"); File.WriteAllText(Path.Combine(p, "运行 app.py"), "raise Exception('must not execute')");
            var s = ProjectSetup.Create(Path.Combine(p, "launcher.yaml"), v, "运行 app.py");
            Eq("existing", s.Config.Runtime.Mode); Eq("my env", s.Config.Runtime.Venv); Eq(".", s.Config.Runtime.ProjectDir);
            True(s.Config.Actions[0].Argv.SequenceEqual(new[] { "python", "运行 app.py" }));
            Eq(0, s.Config.Actions[0].Parameters!.Count); True(new ProjectEnvironment(s.Config, s.Path).Exists);
        });
        Test("setup binds external environment with an absolute path", () => {
            var p = SetupProject(); var v = StubEnvironment(SetupProject(), "external env");
            var s = ProjectSetup.Create(Path.Combine(p, "launcher.yaml"), v, null);
            Eq(v, s.Config.Runtime.Venv); Eq(v, new ProjectEnvironment(s.Config, s.Path).EnvironmentDirectory);
        });
        Test("setup without environment creates only configuration and no fake business entry", () => {
            var p = SetupProject(); var s = ProjectSetup.Create(Path.Combine(p, "launcher.yaml"), null, null);
            Eq("venv", s.Config.Runtime.Mode); Eq(".venv", s.Config.Runtime.Venv);
            Eq(0, s.Config.Actions.Count); Eq(0, ConfigStore.Load(s.Path).Config.Actions.Count);
            True(Directory.GetFileSystemEntries(p).Select(Path.GetFileName).SequenceEqual(new[] { "launcher.yaml" }));
            Throws(() => new ProjectEnvironment(s.Config, s.Path).RequirePython());
        });
        Test("setup preserves existing config verbatim before discovering or binding", () => {
            var p = SetupProject(); var path = Path.Combine(p, "Launcher.yaml"); var original = ConfigStore.Serialize(Simple()) + "\n# keep comment\n";
            File.WriteAllText(path, original); var requested = OperatingSystem.IsWindows() ? Path.Combine(p, "launcher.yaml") : path;
            var s = ProjectSetup.Create(requested, "nonexistent", "nonexistent.py");
            Eq("测试", s.Config.App.Name); Eq(original, File.ReadAllText(path)); Eq(1, Directory.GetFiles(p).Length);
        });
        Test("setup rejects incomplete or removed environment without writing config", () => {
            var p = SetupProject(); var v = StubEnvironment(p, "env");
            File.Delete(Path.Combine(v, "pyvenv.cfg"));
            Throws(() => ProjectSetup.Create(Path.Combine(p, "launcher.yaml"), v, null));
            True(!File.Exists(Path.Combine(p, "launcher.yaml")));
        });
        Test("setup rejects nonexistent entry without writing config", () => {
            var p = SetupProject(); Throws(() => ProjectSetup.Create(Path.Combine(p, "launcher.yaml"), null, "missing.py"));
            True(!File.Exists(Path.Combine(p, "launcher.yaml")));
        });
        Test("setup rejects launcher internal environment", () => {
            var p = SetupProject(); var v = StubEnvironment(p, Path.Combine(".launcher", "runtime"));
            Throws(() => ProjectSetup.Create(Path.Combine(p, "launcher.yaml"), v, null));
            True(!File.Exists(Path.Combine(p, "launcher.yaml")));
        });
        Test("create-only atomic write never overwrites a newly appeared file", () => {
            var p = Path.Combine(SetupProject(), "launcher.yaml"); File.WriteAllText(p, "original");
            try { AtomicFile.Write(p, "replacement", overwrite: false); throw new Exception("Expected IOException"); }
            catch (IOException) { }
            Eq("original", File.ReadAllText(p)); Eq(1, Directory.GetFiles(Path.GetDirectoryName(p)!).Length);
        });
    }
}
