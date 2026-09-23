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
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            WriteCrash(args.Exception); args.Handled = true;
            MessageBox.Show("启动器遇到未处理的错误。\n\n" + args.Exception.Message + "\n\n诊断记录位于 %LOCALAPPDATA%\\ProjectLauncher\\crashes。请附上记录反馈；任务可能未完成。", "Project Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
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
                        if (++i >= e.Args.Length) throw new ConfigException("--project 后需要项目目录或配置文件路径。"); location = e.Args[i]; break;
                    case "--ui-smoke-test":
                        if (++i >= e.Args.Length) throw new ConfigException("--ui-smoke-test 后需要截图与报告输出目录。"); screenshots = e.Args[i]; _smokeMode = true; break;
                    default: throw new ConfigException("未知启动参数：" + e.Args[i]);
                }
            }
            var path = Path.GetFullPath(Directory.Exists(location) ? Path.Combine(location, "launcher.yaml") : location);
            if (!File.Exists(path))
            {
                if (_smokeMode) throw new ConfigException("UI 测试要求已有 launcher.yaml。");
                if (!CreateInitialConfig(ref path)) { Shutdown(); return; }
            }
            ConfigSnapshot? snapshot = null;
            while (snapshot is null)
            {
                try { snapshot = ConfigStore.Load(path); _ = new ProjectEnvironment(snapshot.Config, snapshot.Path); }
                catch (Exception ex) when (!_smokeMode)
                {
                    if (!Dialogs.Confirm(null, "配置无法加载", path + "\n\n" + ex.Message + "\n\n可在文本编辑器中修复 launcher.yaml；本窗口会保留原文件，不自动重置。", "打开配置修复")) { Shutdown(1); return; }
                    Process.Start(new ProcessStartInfo("notepad.exe") { ArgumentList = { path }, UseShellExecute = false });
                    if (!Dialogs.Confirm(null, "修复后重新读取", "请保存文本编辑器中的修改，再点击重新读取。", "重新读取")) { Shutdown(1); return; }
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
            if (!_smokeMode) MessageBox.Show(ex.Message, "Project Launcher 无法启动", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    private static bool CreateInitialConfig(ref string path)
    {
        var folder = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(folder))
        {
            var choose = new OpenFolderDialog { Title = "选择已有 Python 项目目录" };
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
