using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ProjectLauncher.Core;
using ProjectLauncher.Desktop.Controls;

namespace ProjectLauncher.Desktop.Services;

internal static class ToastSmoke
{
    public static async Task RunAsync(string directory, List<string> results)
    {
        var root = Path.Combine(directory, "toast-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var snapshot = ConfigStore.Save(Path.Combine(root, "launcher.yaml"),
            new LauncherConfig { Actions = [new() { Id = "run", Parameters = [] }] }, null);
        var original = File.ReadAllText(snapshot.Path);
        var window = new MainWindow(snapshot); window.Show();
        try
        {
            window.ShowPage("settings"); window.SettingsPage.SelectDesigner();
            await Idle(window);
            var page = (ContentControl)window.FindName("PageHost");
            foreach (var theme in new[] { "dark", "light" })
            {
                ThemeService.Apply(theme); window.Width = 1060; window.Height = 700;
                window.Shell.DismissNotification(); await Idle(window);
                var before = Bounds(page, window);
                var field = Descendants<TextBox>(page).First(t => t.IsVisible);
                field.Focus(); var focused = System.Windows.Input.Keyboard.FocusedElement;
                window.Shell.Notify(new string('X', 1200)); await Idle(window);
                if (Bounds(page, window) != before) throw new Exception("Toast changed page bounds.");
                if (System.Windows.Input.Keyboard.FocusedElement != focused) throw new Exception("Toast stole focus.");
                var toast = (FrameworkElement)window.FindName("Notices");
                if (!toast.IsVisible || toast.ActualHeight > 240) throw new Exception("Toast is missing or unbounded.");
                var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(window); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using (var file = File.Create(Path.Combine(directory, theme + "-toast.png"))) png.Save(file);
                Descendants<Button>(toast).Single(b => Equals(b.Content, "×")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Idle(window);
                if (window.Shell.HasNotification) throw new Exception("Toast close button did not dismiss.");
                if (Bounds(page, window) != before) throw new Exception("Dismissing toast changed page bounds.");
            }
            var settings = window.SettingsPage;
            var workspace = (ActionWorkspace)((ContentControl)settings.FindName("ActionWorkspaceHost")).Content;
            var advanced = Descendants<Expander>(workspace).First(e => Equals(e.Header, "动作高级设置"));
            advanced.IsExpanded = true; await Idle(window);
            var input = Descendants<TextBox>(advanced).First(t => !t.IsReadOnly);
            input.Text = "bad"; await Idle(window);
            if (window.Shell.HasNotification) throw new Exception("Typing should not raise an error toast.");
            var save = (Button)settings.FindName("SaveButton");
            var beforeError = Bounds(workspace, window);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle(window);
                if (!window.Shell.HasNotification) throw new Exception("Invalid save did not notify.");
                if (File.ReadAllText(snapshot.Path) != original) throw new Exception("Invalid draft was persisted.");
                if (Bounds(workspace, window) != beforeError) throw new Exception("Error moved the editor.");
                window.Shell.DismissNotification();
            }
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            input.Text = "0"; await Idle(window);
            if (window.Shell.HasNotification) throw new Exception("Corrected editor error remained visible.");
            results.Add("PASS toast bounds, repeated invalid save, dismissal safety and correction");
        }
        finally { window.SettingsPage.DiscardDirtyMarker(); window.Close(); }
    }
    private static Rect Bounds(FrameworkElement element, Visual parent) =>
        new(element.TransformToAncestor(parent).Transform(new Point()), element.RenderSize);
    private static async Task Idle(Window window) =>
        await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T item) yield return item;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
