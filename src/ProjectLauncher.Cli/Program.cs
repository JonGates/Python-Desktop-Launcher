using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ProjectLauncher.Core;
using ProjectLauncher.Windows;

return await LauncherCli.MainAsync(args);

internal static class LauncherCli
{
    private const string Help = """
Project Launcher CLI · 1.1.0 · Windows

Launcher.Cli.exe check [--project PATH]
Launcher.Cli.exe list [--project PATH]
Launcher.Cli.exe env [--project PATH] [--trust]
Launcher.Cli.exe init --project PATH --trust --yes
Launcher.Cli.exe run ACTION --project PATH --set name=value --trust
Launcher.Cli.exe shell --project PATH --trust [--shell powershell|pwsh|cmd|python]

PATH accepts a project directory or launcher.yaml; defaults to the directory of this executable.
check/list do not run project code. env --trust runs the interpreter probe.
run/init/shell need --trust explicitly; init also needs --yes because it may install dependencies.
--set may be repeated. Unspecified parameters use YAML defaults. Secret values are never saved,
but a secret supplied on the command line may be visible in OS process listings or shell history.
Run actions are noninteractive. Use shell for interactive programs. Exit codes: 0 success,
2 configuration/usage, 124 timeout, 130 cancelled, otherwise the task's nonzero exit code.
""";

    public static async Task<int> MainAsync(string[] args)
    {
        ProjectLauncher.Core.Localization.TextCatalog.Language = ProjectLauncher.Core.Localization.TextCatalog.Normalize(null, System.Globalization.CultureInfo.CurrentUICulture.Name);
        Console.OutputEncoding = new UTF8Encoding(false);
        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h")) { Console.WriteLine(Help); return 0; }
        bool interactive = false; using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; if (!interactive) cancellation.Cancel(); };
        Console.CancelKeyPress += handler;
        ProjectLease? lease = null;
        try
        {
            string verb = args[0], location = AppContext.BaseDirectory, shellChoice = "auto";
            string? actionId = null; bool trust = false, yes = false;
            var values = new Dictionary<string, object?>();
            for (int i = 1; i < args.Length; i++)
            {
                string Next() => ++i < args.Length ? args[i] : throw new ConfigException(new ProjectLauncher.Core.Localization.LocalizedDiagnostic("Error.Cli.0", [args[i - 1]]));
                switch (args[i])
                {
                    case "--project": case "--config": location = Next(); break;
                    case "--trust": trust = true; break;
                    case "--yes": yes = true; break;
                    case "--shell": shellChoice = Next(); break;
                    case "--set":
                        var pair = Next(); int equals = pair.IndexOf('=');
                        if (equals < 1) throw new ConfigException(new ProjectLauncher.Core.Localization.LocalizedDiagnostic("Error.Cli.1", [])); values[pair[..equals]] = pair[(equals + 1)..]; break;
                    default:
                        if (verb == "run" && actionId is null && !args[i].StartsWith('-')) actionId = args[i];
                        else throw new ConfigException(new ProjectLauncher.Core.Localization.LocalizedDiagnostic("Error.Cli.2", [args[i]])); break;
                }
            }
            var path = Path.GetFullPath(Directory.Exists(location) ? Path.Combine(location, "launcher.yaml") : location);
            var snapshot = ConfigStore.Load(path); var runtime = new ProjectEnvironment(snapshot.Config, path);
            foreach (var key in values.Keys) if (!snapshot.Config.Parameters.Any(p => p.Name == key)) throw new ConfigException(new ProjectLauncher.Core.Localization.LocalizedDiagnostic("Error.Cli.3", [key]));
            if (verb is "run" or "init" or "shell" || verb == "env" && trust)
            {
                if (!trust) throw new ConfigException(new ProjectLauncher.Core.Localization.LocalizedDiagnostic("Error.Cli.4", []));
                lease = ProjectLease.Acquire(path);
            }
            switch (verb)
            {
                case "check":
                    Console.WriteLine($"配置有效：{path}\n项目：{snapshot.Config.App.Name}\n根目录：{runtime.Root}\n动作：{snapshot.Config.Actions.Count}；参数：{snapshot.Config.Parameters.Count}\n环境结构存在：{runtime.Exists}\n注意：结构检查不会启动解释器或检查业务依赖。"); return 0;
                case "list":
                    foreach (var a in snapshot.Config.Actions) Console.WriteLine($"{a.Id}\t{a.Label}\t{WindowsArguments.Join(a.Argv)}"); return 0;
                case "env":
                    if (trust) Console.WriteLine(JsonSerializer.Serialize(await runtime.ProbeAsync(cancellation.Token), new JsonSerializerOptions { WriteIndented = true }));
                    else Console.WriteLine($"项目：{runtime.Root}\n虚拟环境：{runtime.EnvironmentDirectory}\n解释器：{runtime.PythonPath}\n结构存在：{runtime.Exists}\nuv：{runtime.UvPath ?? "未找到"}\n加 --trust 才会实际执行解释器检查。");
                    return 0;
                case "init":
                    if (!yes) throw new ConfigException(new ProjectLauncher.Core.Localization.LocalizedDiagnostic("Error.Cli.5", []));
                    foreach (var command in runtime.InitializationPlan())
                    {
                        var code = await RunAsync(command, runtime.Root, runtime.InitializationEnvironment(), cancellation.Token);
                        if (code != 0) return code;
                    }
                    Console.WriteLine("初始化计划执行结束；可用 env --trust 检查实际解释器。"); return 0;
                case "run":
                    actionId ??= snapshot.Config.Actions.FirstOrDefault()?.Id ?? throw new ConfigException(new ProjectLauncher.Core.Localization.LocalizedDiagnostic("Error.Cli.6", []));
                    return await RunAsync(CommandBuilder.Build(snapshot.Config, actionId, values, runtime.Root, runtime.RequirePython()), runtime.Root, runtime.ExecutionEnvironment(), cancellation.Token);
                case "shell":
                    if (shellChoice is not ("auto" or "powershell" or "pwsh" or "cmd" or "python")) throw new ConfigException(new ProjectLauncher.Core.Localization.LocalizedDiagnostic("Error.Cli.7", []));
                    interactive = true;
                    var commandLine = runtime.ShellCommand(shellChoice); var start = new ProcessStartInfo(commandLine[0]) { WorkingDirectory = runtime.Root, UseShellExecute = false };
                    foreach (var arg in commandLine.Skip(1)) start.ArgumentList.Add(arg);
                    start.Environment.Clear(); foreach (var (key, value) in runtime.ExecutionEnvironment()) start.Environment[key] = value;
                    using (var process = Process.Start(start) ?? throw new ConfigException(new ProjectLauncher.Core.Localization.LocalizedDiagnostic("Error.Cli.8", [])))
                    using (var job = new JobObject())
                    {
                        try { job.Attach(process); } catch { try { process.Kill(true); } catch { } throw; }
                        await process.WaitForExitAsync(); return process.ExitCode;
                    }
                default: throw new ConfigException(new ProjectLauncher.Core.Localization.LocalizedDiagnostic("Error.Cli.9", [verb, Help]));
            }
        }
        catch (OperationCanceledException) { return 130; }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 2; }
        finally
        {
            Console.CancelKeyPress -= handler;
            lease?.Dispose();
        }
    }
    private static async Task<int> RunAsync(CommandPlan plan, string cwd, Dictionary<string, string> env, CancellationToken cancellation)
    {
        Console.WriteLine("> " + plan.Preview);
        using var runner = new ProcessRunner(() => new JobObject()); runner.Output += text => Console.Write(text);
        var result = await runner.RunAsync(plan, cwd, env, cancellation);
        return result.TimedOut ? 124 : result.Cancelled ? 130 : result.ExitCode;
    }
}
