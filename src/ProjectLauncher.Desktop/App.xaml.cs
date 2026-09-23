using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using ProjectLauncher.Core;
using ProjectLauncher.Windows;
using ProjectLauncher.Desktop.Services;
using ProjectLauncher.Desktop.Views;

namespace ProjectLauncher.Desktop;

public partial class App : Application
{
    public static App CurrentApp => (App)Current;
    private readonly Dictionary<MainWindow, ProjectLease> _projects = new();
    private bool _smokeMode;
    protected override void OnStartup(StartupEventArgs e)
    {
        var smokeIndex = Array.IndexOf(e.Args, "--ui-smoke-test");
        var preferencePath = smokeIndex >= 0 && smokeIndex + 1 < e.Args.Length
            ? Path.Combine(Path.GetFullPath(e.Args[smokeIndex + 1]), "language-preferences.json")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProjectLauncher", "preferences.json");
        LocalizationService.Current.Initialize(preferencePath, smokeIndex >= 0 ? "zh-CN" : null);
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            WriteCrash(args.Exception); args.Handled = true;
            MessageBox.Show(L.Text("Text.000") + args.Exception.Message + L.Text("Text.001"), "Project Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        };
        TaskScheduler.UnobservedTaskException += (_, args) => { WriteCrash(args.Exception); args.SetObserved(); };
        try
        {
            string location = AppContext.BaseDirectory;
            string? screenshots = null;
            for (int i = 0; i < e.Args.Length; i++)
            {
                switch (e.Args[i])
                {
                    case "--project": case "--config":
                        if (++i >= e.Args.Length) throw new ConfigException(new ProjectLauncher.Core.Localization.LocalizedDiagnostic("Text.002", [])); location = e.Args[i]; break;
                    case "--ui-smoke-test":
                        if (++i >= e.Args.Length) throw new ConfigException(new ProjectLauncher.Core.Localization.LocalizedDiagnostic("Text.003", [])); screenshots = e.Args[i]; _smokeMode = true; break;
                    default: throw new ConfigException(L.Text("Text.004") + e.Args[i]);
                }
            }
            var path = Path.GetFullPath(Directory.Exists(location) ? Path.Combine(location, "launcher.yaml") : location);
            if (!File.Exists(path))
            {
                if (_smokeMode) throw new ConfigException(new ProjectLauncher.Core.Localization.LocalizedDiagnostic("Text.005", []));
                if (!CreateInitialConfig(ref path)) { Shutdown(); return; }
            }
            ConfigSnapshot? snapshot = null;
            while (snapshot is null)
            {
                try { snapshot = ConfigStore.Load(path); _ = new ProjectEnvironment(snapshot.Config, snapshot.Path); }
                catch (Exception ex) when (!_smokeMode)
                {
                    if (!Dialogs.Confirm(null, L.Text("Text.006"), path + "\n\n" + ex.Message + L.Text("Text.007"), L.Text("Text.008"))) { Shutdown(1); return; }
                    Process.Start(new ProcessStartInfo("notepad.exe") { ArgumentList = { path }, UseShellExecute = false });
                    if (!Dialogs.Confirm(null, L.Text("Text.009"), L.Text("Text.010"), L.Text("Text.011"))) { Shutdown(1); return; }
                }
            }
            var window = OpenProject(snapshot);
            if (screenshots is not null)
            {
                var directory = Path.GetFullPath(screenshots);
                _ = Dispatcher.InvokeAsync(async () =>
                {
                    int code;
                    try { code = await UiSmokeRunner.RunAsync(window, directory); }
                    catch (Exception ex) { Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "ui-failure.txt"), ex.ToString()); WriteCrash(ex); code = 1; }
                    Shutdown(code);
                }, System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }
        catch (Exception ex)
        {
            WriteCrash(ex);
            if (!_smokeMode) MessageBox.Show(ex.Message, L.Text("Text.012"), MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    private static bool CreateInitialConfig(ref string path)
    {
        var folder = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(folder))
        {
            var choose = new OpenFolderDialog { Title = L.Text("Text.013") };
            if (choose.ShowDialog() != true) return false;
            folder = choose.FolderName; path = Path.Combine(folder, "launcher.yaml");
            if (File.Exists(path)) return true;
        }
        return new ProjectSetupWindow(path).ShowDialog() == true;
    }
    public MainWindow OpenProject(ConfigSnapshot snapshot)
    {
        var existing = _projects.Keys.FirstOrDefault(w => ProjectEnvironment.SamePath(w.Shell.Snapshot.Path, snapshot.Path));
        if (existing is not null) { existing.WindowState = WindowState.Normal; existing.Activate(); return existing; }
        var mutex = ProjectLease.Acquire(snapshot.Path);
        MainWindow window;
        try { window = new MainWindow(snapshot); }
        catch { mutex.Dispose(); throw; }
        _projects.Add(window, mutex); MainWindow = window;
        window.Closed += (_, _) => { if (_projects.Count == 0 && !_smokeMode) Shutdown(); };
        window.Show(); return window;
    }
    public void ReleaseProject(MainWindow window)
    {
        if (!_projects.Remove(window, out var mutex)) return;
        mutex.Dispose();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        foreach (var window in _projects.Keys.ToList())
        {
            try { window.Shell.Dispose(); _ = window.TerminalPage.DisposeAsync(); } catch { }
            ReleaseProject(window);
        }
        base.OnExit(e);
    }
    internal static void WriteCrash(Exception e)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProjectLauncher", "crashes");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".txt"), e.ToString());
        }
        catch { /* Never recursively crash while writing a diagnostic file. */ }
    }
}
