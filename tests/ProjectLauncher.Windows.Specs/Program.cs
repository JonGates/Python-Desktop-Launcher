using System.Diagnostics;
using System.Text;
using ProjectLauncher.Core;
using ProjectLauncher.Windows;

if (!OperatingSystem.IsWindows()) { Console.WriteLine("SKIP: Windows integration tests require Windows."); return 2; }
const string redirectedConPtyArgument = "--redirected-conpty-child";
if (args.Contains(redirectedConPtyArgument, StringComparer.Ordinal))
{
    try { await VerifyConPtyAsync(); Console.WriteLine("PASS redirected ConPTY child"); return 0; }
    catch (Exception e) { Console.WriteLine("FAIL redirected ConPTY child: " + e); return 1; }
}
int failures = 0;
try
{
    await VerifyConPtyAsync();
    Console.WriteLine("PASS ConPTY starts, inherits project environment, preserves shell state, resizes, closes");
}
catch (Exception e) { failures++; Console.WriteLine("FAIL ConPTY: " + e); }
try
{
    var executable = Environment.ProcessPath ?? throw new Exception("Cannot locate the Windows specs executable.");
    var start = new ProcessStartInfo(executable)
    {
        UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
    };
    start.ArgumentList.Add(redirectedConPtyArgument);
    using var child = Process.Start(start) ?? throw new Exception("Cannot start the redirected ConPTY child.");
    child.StandardInput.Close();
    var outputTask = child.StandardOutput.ReadToEndAsync();
    var errorTask = child.StandardError.ReadToEndAsync();
    await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
    var output = await outputTask; var error = await errorTask;
    if (child.ExitCode != 0) throw new Exception($"Redirected child exited {child.ExitCode}:\n{output}\n{error}");
    Console.WriteLine("PASS ConPTY ignores redirected parent standard handles");
}
catch (Exception e) { failures++; Console.WriteLine("FAIL redirected ConPTY: " + e); }
try
{
    using var job = new JobObject();
    using var p = Process.Start(new ProcessStartInfo
    {
        FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
        Arguments = "/d /c ping -n 30 127.0.0.1 >nul", UseShellExecute = false, CreateNoWindow = true
    })!;
    job.Attach(p); job.Dispose();
    await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
    if (!p.HasExited) throw new Exception("Job-owned process did not exit.");
    Console.WriteLine("PASS Job Object terminates its owned process on close");
}
catch (Exception e) { failures++; Console.WriteLine("FAIL Job Object: " + e); }
try
{
    string path = Path.Combine(Path.GetTempPath(), "lease-test-" + Guid.NewGuid().ToString("N"), "launcher.yaml");
    using (var first = ProjectLease.Acquire(path))
    {
        bool rejected = false;
        try { using var second = ProjectLease.Acquire(path); } catch (ConfigException) { rejected = true; }
        if (!rejected) throw new Exception("Concurrent owner was not refused.");
        await Task.Delay(10); // The owner thread must remain valid across async continuations.
    }
    using var third = ProjectLease.Acquire(path);
    Console.WriteLine("PASS per-project lease rejects concurrent owners and releases across await");
}
catch (Exception e) { failures++; Console.WriteLine("FAIL project lease: " + e); }
Console.WriteLine($"RESULT: {failures} failure(s)");
return failures == 0 ? 0 : 1;

static async Task VerifyConPtyAsync()
{
    var text = new StringBuilder(); var gate = new object();
    await using var terminal = new ConPtySession();
    terminal.Output += s => { lock (gate) text.Append(s); };
    var env = ProjectEnvironment.CleanEnvironment(); env["LAUNCHER_TEST_MARKER"] = "project-environment-marker";
    var cmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
    terminal.Start([cmd, "/d", "/q"], Path.GetTempPath(), env, 100, 30);
    terminal.Write("echo %LAUNCHER_TEST_MARKER%\r");
    terminal.Write("set PROJECT_LOCAL_STATE=persistent-value\r");
    terminal.Write("echo --VALUE:%PROJECT_LOCAL_STATE%:END--\r");
    terminal.Resize(110, 32);
    var deadline = DateTime.UtcNow.AddSeconds(15);
    bool success = false;
    while (DateTime.UtcNow < deadline)
    {
        lock (gate) success = text.ToString().Contains("project-environment-marker") && text.ToString().Contains("--VALUE:persistent-value:END--");
        if (success) break; await Task.Delay(100);
    }
    if (!success) throw new Exception("ConPTY environment or persistent state not observed: " + text);
    await terminal.CloseAsync().WaitAsync(TimeSpan.FromSeconds(15));
}
