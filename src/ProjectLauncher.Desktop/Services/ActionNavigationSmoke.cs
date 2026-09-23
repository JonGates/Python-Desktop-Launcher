using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ProjectLauncher.Core;
using ProjectLauncher.Desktop.Controls;

namespace ProjectLauncher.Desktop.Services;

internal static class ActionNavigationSmoke
{
    public static async Task RunAsync(MainWindow window, string directory, List<string> results)
    {
        if (window.FindName("SidebarProjectName") is not TextBlock projectName || projectName.Text != window.Shell.ProjectName)
            throw new Exception("Sidebar project branding is missing.");
        if (window.FindName("SidebarProjectDescription") is not TextBlock description || description.Text != window.Shell.ProjectDescription ||
            window.FindName("SidebarProjectVersion") is not TextBlock version || version.Text != window.Shell.ProjectVersion)
            throw new Exception("Sidebar description/version does not reflect project configuration.");
        if (window.SettingsPage.FindName("ActionWorkspaceHost") is not ContentControl)
            throw new InvalidOperationException("Unified action editor is missing.");
        await ActionEditorSmoke.RunAsync(directory, results);
        if (window.FindName("ActionList") is not ListBox list || window.FindName("RunNav") is not ToggleButton toggle)
            throw new InvalidOperationException("Run navigation must expose an expandable action list.");
        if (list.Items.Count != window.Shell.Config.Actions.Count) throw new InvalidOperationException("Navigation actions do not match configuration.");
        window.ShowPage("run");
        var host = (ContentControl)window.FindName("PageHost");
        var run = (UserControl)host.Content;
        var form = (ParameterForm)run.FindName("Form");
        var original = form.GetValues();
        var edited = new Dictionary<string, object?>(original);
        var text = window.Shell.Config.Parameters.FirstOrDefault(p => p.Type == "text" && !p.IsSecret);
        if (text is not null) edited[text.Name] = "导航切换保留参数";
        form.ApplyValues(edited);
        foreach (ActionDefinition action in list.Items)
        {
            list.SelectedItem = action;
            if (((TextBlock)run.FindName("ActionTitle")).Text != action.ToString()) throw new InvalidOperationException("Action title is out of sync.");
        }
        list.SelectedIndex = 0;
        if (text is not null && ValueCodec.Text(form.GetValues().GetValueOrDefault(text.Name)) != "导航切换保留参数")
            throw new InvalidOperationException("Switching actions lost form values.");
        form.ApplyValues(original);
        toggle.IsChecked = false; window.UpdateLayout();
        if (list.IsVisible) throw new InvalidOperationException("Collapsed action list is still visible.");
        toggle.IsChecked = true; window.ShowPage("run");
        results.Add("PASS sidebar action selection, title, collapse, and parameter retention");
        if (Descendants<Button>(window).Any(b => b.Content is string s && s is "接入其他项目" or "切换项目"))
            throw new InvalidOperationException("Removed project switching controls are still present.");
        foreach (var theme in new[] { "dark", "light" })
        {
            ThemeService.Apply(theme); window.Width = 1060; window.Height = 700;
            await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.Render);
            var button = (Button)run.FindName("StartButton");
            var origin = button.TransformToAncestor(window).Transform(new Point());
            if (!button.IsVisible || button.ActualHeight < 30 || origin.Y + button.ActualHeight > window.ActualHeight - 28)
                throw new InvalidOperationException("Start button is clipped at the minimum window size.");
            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(directory, theme + "-run-compact.png")); encoder.Save(file);
            results.Add("PASS compact action layout at 1060x700: " + theme);
        }
        window.Width = 1360; window.Height = 920;
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
