using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ProjectLauncher.Core;
using ProjectLauncher.Desktop.Controls;

namespace ProjectLauncher.Desktop.Services;

internal static class ActionQuickControlSmoke
{
    public static async Task RunAsync(string directory, List<string> results)
    {
        var root = Path.Combine(directory, "quick-actions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".venv", "Scripts"));
        File.WriteAllText(Path.Combine(root, ".venv", "Scripts", "python.exe"), "fixture: never executed");
        File.WriteAllText(Path.Combine(root, ".venv", "pyvenv.cfg"), "home = fixture; interpreter is not executed");
        File.WriteAllText(Path.Combine(root, "job.ps1"), "param([string]$Message)\nWrite-Output ('VALUE=' + $Message)\nStart-Sleep -Seconds 30\n");
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var config = new LauncherConfig { Parameters = [new() { Name = "message", Label = "消息", Binding = "positional", Default = "default" }], Actions = [
            new() { Id = "service", Label = "服务", Argv = [powershell, "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", "job.ps1"], Parameters = ["message"] },
            new() { Id = "app", Label = "应用", Argv = [powershell, "-NoProfile", "-NonInteractive", "-Command", "exit 0"], Parameters = [] }] };
        var snapshot = ConfigStore.Save(Path.Combine(root, "launcher.yaml"), config, null);
        var window = new MainWindow(snapshot); window.Show();
        try
        {
            window.Shell.State.TrustedHash = snapshot.Hash;
            await Idle(window);
            var list = (ListBox)window.FindName("ActionList");
            Button ButtonFor(string id) => Descendants<Button>(list).Single(b => b.Name == "ActionQuickButton" && b.DataContext is ActionDefinition a && a.Id == id);
            var start = Descendants<Button>(list).FirstOrDefault(b => b.Name == "ActionQuickButton");
            if (start is null) throw new Exception("Sidebar action start/stop control is missing.");
            var host = (ContentControl)window.FindName("PageHost");
            var form = (ParameterForm)((UserControl)host.Content).FindName("Form");
            form.ApplyValues(new() { ["message"] = "quick-action-current-value" });
            window.ShowPage("terminal"); await Idle(window);
            ButtonFor("service").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => window.Shell.LogText.Contains("VALUE=quick-action-current-value") && window.Shell.Busy);
            await Idle(window);
            if (!ButtonFor("service").IsEnabled || ButtonFor("app").IsEnabled) throw new Exception($"Running action controls mismatch: busy={window.Shell.Busy}, active={window.Shell.ActiveActionId}, service enabled={ButtonFor("service").IsEnabled}, tag={ButtonFor("service").Tag}, content={ButtonFor("service").Content}, app enabled={ButtonFor("app").IsEnabled}.");
            if (!Equals(ButtonFor("service").Content, "●")) throw new Exception("Running action lacks stop indicator.");
            var glyph = Descendants<TextBlock>(ButtonFor("service")).First(t => t.Text == "●");
            if (((SolidColorBrush)glyph.Foreground).Color != ((SolidColorBrush)window.FindResource("DangerBrush")).Color) throw new Exception("Stop circle is not rendered in the danger color.");
            window.ShowPage("terminal"); await Idle(window);
            foreach (var theme in new[] { "dark", "light" })
            {
                ThemeService.Apply(theme); await Idle(window);
                var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(Path.Combine(directory, theme + "-sidebar-running.png")); png.Save(output);
            }
            ConfirmStop(window, ButtonFor("service"), false);
            if (!window.Shell.Busy) throw new Exception("Cancelling stop terminated the action.");
            ConfirmStop(window, ButtonFor("service"), true);
            await Until(() => !window.Shell.Busy); await Idle(window);
            if (window.Shell.History.First().Status != "已停止") throw new Exception("Sidebar stop did not terminate the selected job.");
            if (!ButtonFor("service").IsEnabled || !Equals(ButtonFor("service").Content, "▶")) throw new Exception("Idle start state was not restored.");
            ButtonFor("app").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => !window.Shell.Busy && window.Shell.History.Any(h => h.ActionId == "app"));
            if (window.Shell.History.First().Status != "成功") throw new Exception("Natural completion failed.");
            results.Add("PASS sidebar start uses current parameters, stop/cancel from terminal, per-action busy guard and completion reset");
        }
        finally { if (window.Shell.Busy) { window.Shell.Stop(); await Until(() => !window.Shell.Busy); } window.Close(); await Idle(window); }
    }
    private static void ConfirmStop(Window owner, Button button, bool accept)
    {
        Exception? error = null;
        owner.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => {
            var dialog = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.Owner == owner && w.Title == "停止当前任务？");
            try { if (dialog is null) throw new Exception("Stop confirmation missing."); dialog.DialogResult = accept; }
            catch (Exception ex) { error = ex; dialog?.Close(); }
        }));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (error is not null) throw error;
    }
    private static async Task Until(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(12);
        while (!condition()) { if (DateTime.UtcNow > until) throw new TimeoutException("Sidebar action workflow did not settle."); await Task.Delay(50); }
    }
    private static Task Idle(Window window) => window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ContextIdle).Task;
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is T item) yield return item; foreach (var nested in Descendants<T>(child)) yield return nested; }
    }
}
