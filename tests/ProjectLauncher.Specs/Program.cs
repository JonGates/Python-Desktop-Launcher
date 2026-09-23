using System.Globalization;
using ProjectLauncher.Core;

var suite = new SpecSuite();
await suite.RunAsync();
return suite.Failed == 0 ? 0 : 1;

sealed partial class SpecSuite
{
    public int Failed { get; private set; }
    private int _passed;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Launcher specs 中文 " + Guid.NewGuid().ToString("N"));
    private LauncherConfig Simple() => new()
    {
        App = new() { Name = "测试" }, Runtime = new() { Mode = "venv", Requirements = "" },
        Actions = [new() { Id = "run", Label = "运行", Argv = ["python", "main.py"] }],
        Parameters = [new() { Name = "workers", Type = "integer", Argument = "--workers", Default = 4, Min = 1, Max = 16 }]
    };
    private void Test(string name, Action test)
    {
        try { test(); Console.WriteLine("PASS " + name); _passed++; }
        catch (Exception e) { Console.WriteLine("FAIL " + name + ": " + e); Failed++; }
    }
    private async Task TestAsync(string name, Func<Task> test)
    {
        try { await test(); Console.WriteLine("PASS " + name); _passed++; }
        catch (Exception e) { Console.WriteLine("FAIL " + name + ": " + e); Failed++; }
    }
    private static void True(bool value, string reason = "assertion failed") { if (!value) throw new Exception(reason); }
    private static void Eq<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
    private static void Throws(Action action) { try { action(); } catch (ConfigException) { return; } throw new Exception("Expected ConfigException"); }
    public async Task RunAsync()
    {
        Directory.CreateDirectory(_root);
        try
        {
            Test("legacy v1 configuration is readable", () => {
                var c = ConfigStore.Load(Path.Combine(AppContext.BaseDirectory, "fixtures", "legacy-v1.yaml")).Config;
                Eq(1, c.SchemaVersion); Eq("run", c.Actions[0].Id); True(c.Parameters.Count >= 9);
            });
            Test("serialize / deserialize preserves zero and false", () => {
                var c = Simple(); c.Parameters[0].Default = 0; c.Parameters[0].Min = 0;
                c.Parameters.Add(new() {Name="flag", Type="boolean", Argument="--flag", Default=false});
                var d = ConfigStore.Parse(ConfigStore.Serialize(c)); Eq("0", ValueCodec.Text(d.Parameters[0].Default)); Eq("false", ValueCodec.Text(d.Parameters[1].Default));
            });
            Test("duplicate names rejected", () => {var c=Simple(); c.Parameters.Add(c.Parameters[0]); Throws(()=>ConfigValidator.Validate(c));});
            Test("unknown action reference rejected", () => {var c=Simple(); c.Actions[0].Parameters=["missing"]; Throws(()=>ConfigValidator.Validate(c));});
            Test("unknown YAML fields rejected", () => Throws(()=>ConfigStore.Parse("schema_version: 1\nunknown: typo\n")));
            Test("reserved environment blocked case-insensitively", () => {var c=Simple(); c.Runtime.Env["Path"]="bad"; Throws(()=>ConfigValidator.Validate(c));});
            Test("reserved parameter environment blocked", () => {var c=Simple(); c.Parameters[0].Binding="env"; c.Parameters[0].EnvName="PYTHONPATH"; Throws(()=>ConfigValidator.Validate(c));});
            Test("duplicate YAML keys rejected", () => Throws(()=>ConfigStore.Parse("schema_version: 1\nschema_version: 1\n")));
            Test("bad bounds rejected", () => {var c=Simple();c.Parameters[0].Min=20;Throws(()=>ConfigValidator.Validate(c));});
            Test("unknown visibility reference rejected", () => {var c=Simple();c.Parameters[0].VisibleWhen["missing"]=true;Throws(()=>ConfigValidator.Validate(c));});
            Test("argv keeps spaces and injection-looking text as data", () => {
                var c=Simple(); c.Parameters=[new(){Name="input",Argument="--input",Type="text"}];
                var cmd=CommandBuilder.Build(c,"run",new(){{"input","hello & echo BAD \"quoted\""}},_root,"project-python");
                Eq("project-python",cmd.Argv[0]);Eq("hello & echo BAD \"quoted\"",cmd.Argv[3]);Eq(4,cmd.Argv.Count);
            });
            Test("explicit empty parameters means none", () => {var c=Simple();c.Actions[0].Parameters=[];Eq(2,CommandBuilder.Build(c,"run",new(),_root,"python").Argv.Count);});
            Test("missing parameters list uses all defaults", () => {var cmd=CommandBuilder.Build(Simple(),"run",new(),_root,"python");Eq("--workers",cmd.Argv[2]);Eq("4",cmd.Argv[3]);});
            Test("explicit argument order preserved", () => {
                var c=Simple();c.Parameters.Add(new(){Name="source",Binding="positional",Default="file.txt"});c.Actions[0].Parameters=["source","workers"];
                var a=CommandBuilder.Build(c,"run",new(),_root,"python").Argv;Eq("file.txt",a[2]);Eq("--workers",a[3]);
            });
            Test("boolean false emits false_argument", () => {var c=Simple();c.Parameters=[new(){Name="flag",Type="boolean",Argument="--yes",FalseArgument="--no",Default=false}];Eq("--no",CommandBuilder.Build(c,"run",new(),_root,"python").Argv[2]);});
            Test("boolean value emits explicit false", () => {var c=Simple();c.Parameters=[new(){Name="flag",Type="boolean",BooleanMode="value",Argument="--enabled",Default=false}];Eq("false",CommandBuilder.Build(c,"run",new(),_root,"python").Argv[3]);});
            Test("hidden required parameter is not validated or passed", () => {var c=Simple(); c.Parameters.Add(new(){Name="hidden",Argument="--hidden",Required=true,VisibleWhen=new(){{"workers",100}}});Eq(4,CommandBuilder.Build(c,"run",new(),_root,"python").Argv.Count);});
            Test("advanced parameters still passed", () => {var c=Simple();c.Parameters[0].Advanced=true;Eq(4,CommandBuilder.Build(c,"run",new(),_root,"python").Argv.Count);});
            Test("range checked at execution", () => Throws(()=>CommandBuilder.Build(Simple(),"run",new(){{"workers",0}},_root,"python")));
            Test("non-integer rejected", () => Throws(()=>CommandBuilder.Build(Simple(),"run",new(){{"workers","3.4"}},_root,"python")));
            Test("relative paths anchored at project", () => {var c=Simple();c.Parameters=[new(){Name="file",Type="file",Argument="--file",Default="输入.txt"}];Eq(Path.Combine(_root,"输入.txt"),CommandBuilder.Build(c,"run",new(),_root,"python").Argv[3]);});
            Test("must_exist checks type", () => {var c=Simple();c.Parameters=[new(){Name="file",Type="file",Argument="--file",Default=_root,MustExist=true}];Throws(()=>CommandBuilder.Build(c,"run",new(),_root,"python"));});
            Test("pip maps to project interpreter", () => {var c=Simple();c.Actions[0].Argv=["pip","list"];c.Actions[0].Parameters=[];var a=CommandBuilder.Build(c,"run",new(),_root,"EXACT-PYTHON").Argv;True(a.SequenceEqual(new[]{"EXACT-PYTHON","-m","pip","list"}));});
            Test("password preview redacted and env binding not on argv", () => {var c=Simple();c.Parameters=[new(){Name="token",Type="password",Binding="env",EnvName="API_TOKEN"}];var a=CommandBuilder.Build(c,"run",new(){{"token","my-secret"}},_root,"python");Eq(2,a.Argv.Count);Eq("my-secret",a.Environment["API_TOKEN"]);True(!a.Preview.Contains("my-secret"));});
            Test("secret argv preview redacted", () => {var c=Simple();c.Parameters=[new(){Name="token",Type="password",Argument="--token"}];var a=CommandBuilder.Build(c,"run",new(){{"token","special-token"}},_root,"python");True(!a.Preview.Contains("special-token"));True(a.Secrets.Contains("special-token"));});
            Test("empty optional omitted but zero retained", () => {var c=Simple();c.Parameters[0].Min=0;Eq("0",CommandBuilder.Build(c,"run",new(){{"workers",0}},_root,"python").Argv[3]);Eq(2,CommandBuilder.Build(c,"run",new(){{"workers",""}},_root,"python").Argv.Count);});
            Test("NaN rejected", () => {var c=Simple();c.Parameters[0].Type="number";Throws(()=>CommandBuilder.Build(c,"run",new(){{"workers","NaN"}},_root,"python"));});
            Test("NUL rejected", () => {var c=Simple();c.Actions[0].Argv.Add("a\0b");Throws(()=>ConfigValidator.Validate(c));});
            Test("save rejects external edits", () => {var path=Path.Combine(_root,"conflict.yaml");File.WriteAllText(path,ConfigStore.Serialize(Simple()));var s=ConfigStore.Load(path);File.AppendAllText(path,"\n# changed\n");Throws(()=>ConfigStore.Save(path,s.Config,s.Hash));});
            Test("atomic save makes backup", () => {var path=Path.Combine(_root,"backup.yaml");File.WriteAllText(path,ConfigStore.Serialize(Simple()));var s=ConfigStore.Load(path);s.Config.App.Name="changed";ConfigStore.Save(path,s.Config,s.Hash);True(File.Exists(path+".bak"));Eq("changed",ConfigStore.Load(path).Config.App.Name);});
            Test("missing project environment never falls back", () => {var r=new ProjectEnvironment(Simple(),Path.Combine(_root,"launcher.yaml"));Throws(()=>r.RequirePython());});
            Test("project root cannot be venv", () => {var c=Simple();c.Runtime.Venv=".";Throws(()=>new ProjectEnvironment(c,Path.Combine(_root,"launcher.yaml")));});
            Test("launcher runtime cannot be project venv", () => {var c=Simple();c.Runtime.Venv=".launcher/runtime";Throws(()=>new ProjectEnvironment(c,Path.Combine(_root,"launcher.yaml")));});
            Test("secrets removed from stored values and presets", () => {var c=Simple();c.Parameters[0].Secret=true;var st=new UserState();st.Values["workers"]=7;st.Presets["old"]=new(){{"workers",8}};StateStore.Sanitize(st,c);True(!st.Values.ContainsKey("workers"));True(!st.Presets["old"].ContainsKey("workers"));});
            Test("streaming redaction crosses chunk boundary", () => {var r=new StreamingRedactor(["ABCDEF"]);var result=r.Push("before ABC")+r.Push("DEF after")+r.Finish();Eq("before [REDACTED] after",result);});
            Test("multiple overlapping secrets longest first", () => {var r=new StreamingRedactor(["secret","secret-long"]);Eq("[REDACTED]",r.Push("secret-long")+r.Finish());});
            Test("Windows command quoting handles trailing backslashes", () => Eq("\"C:\\with space\\\\\"",WindowsArguments.Quote("C:\\with space\\")));
            Test("Windows command quoting handles empty", () => Eq("\"\"",WindowsArguments.Quote("")));
            Test("progress protocol handles total", () => {var p=ProgressMessage.TryParse("@@launcher:{\"progress\":5,\"total\":20,\"message\":\"ok\"}");Eq(25d,p!.Percent);Eq("ok",p.Message);});
            Test("invalid progress ignored", () => True(ProgressMessage.TryParse("@@launcher:not-json") is null));
            Test("terminal split CSI and color", () => {var s=new TerminalScreen(20,4);s.Feed("\u001b[3");s.Feed("1mHi");Eq("H",s.GetCell(0,0).Text);Eq(0xCD3131,s.GetCell(0,0).Foreground);});
            Test("terminal carriage return replaces line", () => {var s=new TerminalScreen(20,4);s.Feed("abc\rXYZ");True(s.PlainText().StartsWith("XYZ"));});
            Test("terminal cursor addressing", () => {var s=new TerminalScreen(20,4);s.Feed("\u001b[2;3HZ");Eq("Z",s.GetCell(2,1).Text);});
            Test("terminal clear screen", () => {var s=new TerminalScreen(20,4);s.Feed("old\u001b[2J\u001b[Hnew");True(s.PlainText().StartsWith("new"));});
            Test("terminal Chinese uses two cells", () => {var s=new TerminalScreen(20,4);s.Feed("中文A");Eq("中",s.GetCell(0,0).Text);True(s.GetCell(1,0).Continuation);Eq("A",s.GetCell(4,0).Text);});
            Test("terminal alternate screen restores main", () => {var s=new TerminalScreen(20,4);s.Feed("main\u001b[?1049halt\u001b[?1049l");True(s.PlainText().StartsWith("main"));});
            Test("terminal bracketed paste mode", () => {var s=new TerminalScreen(20,4);s.Feed("\u001b[?2004h");True(s.BracketedPaste);});
            Test("terminal DSR responds", () => {var s=new TerminalScreen(20,4);string answer="";s.ResponseRequested+=x=>answer=x;s.Feed("abc\u001b[6n");Eq("\u001b[1;4R",answer);});
            Test("terminal OSC52 ignored", () => {var s=new TerminalScreen(20,4);s.Feed("\u001b]52;c;c2VjcmV0\aok");True(s.PlainText().StartsWith("ok"));});
            Test("terminal history bounded", () => {var s=new TerminalScreen(20,3,10);for(int i=0;i<100;i++)s.Feed(i+"\r\n");True(s.HistoryCount<=10);});
            Test("terminal resizes safely", () => {var s=new TerminalScreen(20,4);s.Feed("abc");s.Resize(40,8);Eq("a",s.GetCell(0,0).Text);Eq(40,s.Columns);});
            Test("deployment refuses collisions without changing original", () => {var d=Path.Combine(_root,"deploy");Directory.CreateDirectory(d);File.WriteAllText(Path.Combine(d,"Launcher.exe"),"original");var src=Path.Combine(_root,"source.exe");File.WriteAllText(src,"new");Throws(()=>ProjectInstaller.Install(src,d,"main.py"));Eq("original",File.ReadAllText(Path.Combine(d,"Launcher.exe")));});
            Test("deployment retains existing launcher.yaml", () => {var d=Path.Combine(_root,"deploy2");Directory.CreateDirectory(d);var text=ConfigStore.Serialize(Simple());File.WriteAllText(Path.Combine(d,"launcher.yaml"),text);var src=Path.Combine(_root,"source.exe");File.WriteAllText(src,"new");ProjectInstaller.Install(src,d,"main.py");Eq(text,File.ReadAllText(Path.Combine(d,"launcher.yaml")));True(!Directory.Exists(Path.Combine(d,".venv")));});
            Test("progress array ignored", () => True(ProgressMessage.TryParse("@@launcher:[]") is null));
            Test("progress string ignored", () => True(ProgressMessage.TryParse("@@launcher:{\"progress\":\"x\"}") is null));
            Test("progress non-number total ignored", () => True(ProgressMessage.TryParse("@@launcher:{\"progress\":3,\"total\":false}") is null));
            Test("command executable selected from child PATH", () => {
                var d=Path.Combine(_root,"only-child-path");Directory.CreateDirectory(d);
                var name=OperatingSystem.IsWindows()?"probe.exe":"probe";var file=Path.Combine(d,name);File.WriteAllText(file,"stub for path resolution only");
                Eq(file,ProjectEnvironment.ResolveExecutable("probe",_root,new Dictionary<string,string>{{"PATH",d}}));
            });
            Test("missing executable rejected without system fallback", () => Throws(()=>ProjectEnvironment.ResolveExecutable("absolutely_missing_launcher_123",_root,new Dictionary<string,string>{{"PATH",""}})));
            Test("launcher state root cannot be project venv", () => {var c=Simple();c.Runtime.Venv=".launcher";Throws(()=>new ProjectEnvironment(c,Path.Combine(_root,"launcher.yaml")));});
            Test("state Int64 identity preserved", () => {using var json=System.Text.Json.JsonDocument.Parse("9223372036854775806");Eq("9223372036854775806",ValueCodec.Text(ValueCodec.Unwrap(json.RootElement)));});
            Test("discovery binds an existing custom environment directory", () => {
                var d = Path.Combine(_root, "custom-environment"); var v = Path.Combine(d, "my env");
                var scripts = Path.Combine(v, OperatingSystem.IsWindows() ? "Scripts" : "bin"); Directory.CreateDirectory(scripts);
                File.WriteAllText(Path.Combine(v, "pyvenv.cfg"), "home = test");
                File.WriteAllText(Path.Combine(scripts, OperatingSystem.IsWindows() ? "python.exe" : "python"), "test fixture, never execute");
                var c = ProjectInstaller.Discover(d); Eq("existing", c.Runtime.Mode); Eq("my env", c.Runtime.Venv);
            });
            Test("discovery does not treat an incomplete environment as existing", () => {
                var d = Path.Combine(_root, "incomplete-environment"); Directory.CreateDirectory(Path.Combine(d, ".venv"));
                File.WriteAllText(Path.Combine(d, ".venv", "pyvenv.cfg"), "home = test");
                Eq("venv", ProjectInstaller.Discover(d).Runtime.Mode);
            });
            RunSetupSpecs();
            var shell=OperatingSystem.IsWindows()?"cmd.exe":"/bin/sh";
            var shellArgs=OperatingSystem.IsWindows()?new[]{"/d","/c","echo process-ok"}:new[]{"-c","printf process-ok"};
            await TestAsync("real process produces captured output and exit code", async () => {using var runner=new ProcessRunner();string output="";runner.Output+=x=>output+=x;var result=await runner.RunAsync(new CommandPlan([shell,..shellArgs],new(),"test",[]),_root,new Dictionary<string,string>(),CancellationToken.None);Eq(0,result.ExitCode);True(output.Contains("process-ok"));});
            await TestAsync("real process nonzero exit is not success", async () => {using var runner=new ProcessRunner();var args=OperatingSystem.IsWindows()?new[]{"/d","/c","exit /b 7"}:new[]{"-c","exit 7"};var r=await runner.RunAsync(new([shell,..args],new(),"bad",[]),_root,new(),CancellationToken.None);Eq(7,r.ExitCode);});
        }
        finally { try { Directory.Delete(_root,true); } catch { } }
        Console.WriteLine($"\nRESULT: {_passed} passed, {Failed} failed");
    }
}
