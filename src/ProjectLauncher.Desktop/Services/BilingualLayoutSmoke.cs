using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ProjectLauncher.Core;

namespace ProjectLauncher.Desktop.Services;

internal static class BilingualLayoutSmoke
{
    public static async Task RunAsync(string directory, List<string> results)
    {
        var root = Path.Combine(directory, "bilingual-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var config = new LauncherConfig {
            App = new() { Name = "Automation Toolkit", Description = "Local services and desktop tools.", Version = "1.0.0" },
            Actions = [new() { Id = "service", Label = "API service", Argv = ["python", "server.py"], Parameters = ["port", "reload"] },
                       new() { Id = "desktop", Label = "Desktop app", Argv = ["python", "app.py"], Parameters = [] }],
            Parameters = [new() { Name = "port", Label = "Port", Type = "integer", Argument = "--port", Default = 8000, Min = 1, Max = 65535 },
                          new() { Name = "reload", Label = "Auto reload", Type = "boolean", Argument = "--reload", Default = true }]
        };
        var snapshot = ConfigStore.Save(Path.Combine(root, "launcher.yaml"), config, null);
        var window = new MainWindow(snapshot); window.Show();
        try
        {
            foreach (var language in new[] { "zh-CN", "en-US" })
            foreach (var theme in new[] { "dark", "light" })
            foreach (var size in new[] { new Size(1060, 700), new Size(1360, 900) })
            {
                LocalizationService.Current.SetLanguage(language); ThemeService.Apply(theme); window.Width = size.Width; window.Height = size.Height;
                foreach (var page in new[] { "run", "terminal", "environment", "settings" })
                {
                    window.ShowPage(page); if (page == "settings") window.SettingsPage.SelectDesigner();
                    await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
                    // Allow the composition frame to catch up after switching culture and theme.
                    await Task.Delay(180);
                    var host = (ContentControl)window.FindName("PageHost");
                    if (page is "run" or "settings")
                    {
                        var button = (Button)((UserControl)host.Content).FindName(page == "run" ? "StartButton" : "SaveButton");
                        var bounds = button.TransformToAncestor(window).TransformBounds(new Rect(button.RenderSize));
                        if (bounds.Right > window.ActualWidth - 8 || bounds.Bottom > window.ActualHeight - 12 || bounds.Left < 200)
                            throw new Exception($"{language} {theme} {page}: primary button clipped.");
                    }
                    Capture(window, Path.Combine(directory, $"{language}-{theme}-{(int)size.Width}-{page}.png"));
                }
                results.Add($"PASS bilingual native layouts: {language}, {theme}, {size.Width}x{size.Height}");
            }
        }
        finally { LocalizationService.Current.SetLanguage("zh-CN"); window.Close(); }
    }
    private static void Capture(Window window, string path)
    {
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
}
