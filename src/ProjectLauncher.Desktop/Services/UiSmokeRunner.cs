using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ProjectLauncher.Desktop.Services;

/// <summary>Runs on an actual Windows desktop. Never marks this test passed without creating real WPF visuals.</summary>
internal static class UiSmokeRunner
{
    public static async Task<int> RunAsync(MainWindow window, string directory)
    {
        Directory.CreateDirectory(directory);
        var log = new StringBuilder(); using var trace = new StringWriter(log);
        using var listener = new TextWriterTraceListener(trace);
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        var old = PresentationTraceSources.DataBindingSource.Switch.Level;
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        var results = new List<string>();
        try
        {
            await ActionNavigationSmoke.RunAsync(window, directory, results);
            await ActionQuickControlSmoke.RunAsync(directory, results);
            await ThemeControlSmoke.RunAsync(directory, results);
            window.Width = 1360; window.Height = 920;
            foreach (string theme in new[] { "dark", "light" })
            {
                ThemeService.Apply(theme);
                foreach (var page in new[] { "run", "terminal", "environment", "settings" })
                {
                    window.ShowPage(page);
                    if (page == "settings") window.SettingsPage.SelectDesigner();
                    if (page == "terminal" && theme == "dark") window.TerminalPage.AddPreviewTab();
                    await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.Render);
                    await Task.Delay(180);
                    if (window.ActualWidth < 1000 || window.ActualHeight < 600) throw new InvalidOperationException("Window did not create a usable native visual.");
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(window);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    var name = $"{theme}-{page}.png";
                    using (var file = File.Create(Path.Combine(directory, name))) encoder.Save(file);
                    results.Add("PASS native WPF layout/render: " + name);
                }
            }
            ProjectSetupSmoke.Run(directory, results);
            listener.Flush();
            if (log.Length > 0) throw new InvalidOperationException("WPF binding errors:\n" + log);
            results.Add("NOT COVERED: interactive ConPTY, IME, real drag-drop, high DPI, foreground focus and UI automation workflows.");
            results.Add("The terminal screenshot uses labeled synthetic rendering-check text, not a live shell.");
            File.WriteAllLines(Path.Combine(directory, "ui-smoke-report.txt"), results);
            return 0;
        }
        catch (Exception ex)
        {
            results.Add("FAIL " + ex); File.WriteAllLines(Path.Combine(directory, "ui-smoke-report.txt"), results); return 1;
        }
        finally
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
            PresentationTraceSources.DataBindingSource.Switch.Level = old;
            window.SettingsPage.DiscardDirtyMarker();
        }
    }
}
