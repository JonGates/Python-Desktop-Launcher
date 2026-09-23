using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ProjectLauncher.Core;
using ProjectLauncher.Desktop.Views;

namespace ProjectLauncher.Desktop.Services;

internal static class ProjectSetupSmoke
{
    public static void Run(string directory, List<string> results)
    {
        var fixtures = Path.Combine(directory, "setup-fixtures-" + Guid.NewGuid().ToString("N"));
        foreach (var theme in new[] { "dark", "light" })
        foreach (var count in new[] { 0, 1, 2 })
        {
            ThemeService.Apply(theme);
            var project = Path.Combine(fixtures, $"{theme}-{count} 中文项目"); Directory.CreateDirectory(project);
            for (int i = 0; i < count; i++)
            {
                var environment = Path.Combine(project, i == 0 ? ".venv" : "another env");
                Directory.CreateDirectory(Path.Combine(environment, "Scripts"));
                File.WriteAllText(Path.Combine(environment, "pyvenv.cfg"), "home = test fixture");
                File.WriteAllText(Path.Combine(environment, "Scripts", "python.exe"), "Rendering fixture only; never execute.");
            }
            File.WriteAllText(Path.Combine(project, "main.py"), "raise Exception('setup must not execute this script')");
            var path = Path.Combine(project, "launcher.yaml");
            var dialog = new ProjectSetupWindow(path);
            RunDialog(dialog, () =>
            {
                Capture(dialog, Path.Combine(directory, $"{theme}-setup-{count}.png"));
                if (count == 2)
                {
                    Click(dialog.CreateButton);
                    if (File.Exists(path) || string.IsNullOrEmpty(dialog.ErrorText.Text)) throw new InvalidOperationException("Ambiguous environment was silently selected.");
                    dialog.EnvironmentBox.SelectedIndex = 1;
                }
                if (dialog.FindName("EntryBox") is not null) throw new InvalidOperationException("Setup still asks for an entry.");
                Click(dialog.CreateButton);
                if (dialog.Snapshot is null) throw new InvalidOperationException("Setup did not create configuration: " + dialog.ErrorText.Text);
            });
            var config = ConfigStore.Load(path).Config;
            if (config.Runtime.Mode != (count == 0 ? "venv" : "existing") ||
                config.Runtime.Venv != (count == 2 ? "another env" : ".venv")) throw new InvalidOperationException("Wrong environment persisted.");
            if (count == 0 && (Directory.Exists(Path.Combine(project, ".venv")) || config.Actions.Count != 0))
                throw new InvalidOperationException("Deferred setup changed environment or fabricated an entry.");
            results.Add($"PASS native setup render and create workflow: {theme}, {count} environment(s)");
        }

        var cancelProject = Path.Combine(fixtures, "cancel"); Directory.CreateDirectory(cancelProject);
        var cancelPath = Path.Combine(cancelProject, "launcher.yaml");
        var cancel = new ProjectSetupWindow(cancelPath);
        RunDialog(cancel, () => cancel.Close());
        if (File.Exists(cancelPath)) throw new InvalidOperationException("Cancelling setup wrote a configuration.");
        results.Add("PASS cancelling setup creates no configuration");

        var race = new ProjectSetupWindow(cancelPath);
        var original = ConfigStore.Serialize(ProjectInstaller.Discover(cancelProject)) + "\n# keep original\n";
        File.WriteAllText(cancelPath, original);
        RunDialog(race, () => Click(race.CreateButton));
        if (File.ReadAllText(cancelPath) != original) throw new InvalidOperationException("Existing configuration was overwritten.");
        results.Add("PASS configuration appearing during setup is preserved");
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void RunDialog(ProjectSetupWindow dialog, Action action)
    {
        Exception? failure = null;
        dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
            finally { if (dialog.IsVisible) dialog.Close(); }
        }, DispatcherPriority.ContextIdle);
        dialog.ShowDialog();
        if (failure is not null) throw new InvalidOperationException("First-run UI test failed.", failure);
    }

    private static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        var image = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        image.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path); encoder.Save(file);
    }
}
