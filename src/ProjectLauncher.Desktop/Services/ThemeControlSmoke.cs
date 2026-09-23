using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ProjectLauncher.Desktop.Services;

internal static class ThemeControlSmoke
{
    public static async Task RunAsync(string directory, List<string> results)
    {
        var tabs = new TabControl { Height = 80, SelectedIndex = 1 };
        foreach (var title in new[] { "基本信息", "启动动作", "完整 YAML" }) tabs.Items.Add(new TabItem { Header = title });
        var scroller = new ScrollViewer { Height = 180, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new Border { Width = 1200, Height = 1200, Background = Brushes.Transparent } };
        var panel = new StackPanel { Margin = new Thickness(24) }; panel.Children.Add(tabs); panel.Children.Add(scroller);
        var window = new Window { Width = 640, Height = 380, Content = panel, Title = "控件布局验证" }; window.Show();
        try
        {
            foreach (var theme in new[] { "dark", "light" })
            {
                ThemeService.Apply(theme); await Idle(window);
                var selected = (TabItem)tabs.Items[1]; var next = (TabItem)tabs.Items[2];
                double right = selected.TransformToAncestor(tabs).Transform(new Point(selected.ActualWidth, 0)).X;
                double left = next.TransformToAncestor(tabs).Transform(new Point()).X;
                if (left - right < 5) throw new Exception($"Tab header right edge overlaps adjacent tab: gap={left - right}.");
                var bars = Descendants<ScrollBar>(scroller).Where(b => b.IsVisible).ToArray();
                if (bars.Length != 2) throw new Exception("Expected both scrollbar orientations.");
                foreach (var bar in bars)
                {
                    if (bar.Template.FindName("ScrollChrome", bar) is not Grid) throw new Exception("Scrollbar fell back to system chrome.");
                    var track = (Track)bar.Template.FindName("PART_Track", bar);
                    bool vertical = bar.Orientation == Orientation.Vertical;
                    if ((vertical ? bar.ActualWidth : bar.ActualHeight) > 12.5) throw new Exception("Scrollbar exceeds compact track width.");
                    scroller.ScrollToVerticalOffset(150); scroller.ScrollToHorizontalOffset(150); await Idle(window);
                    double before = vertical ? scroller.VerticalOffset : scroller.HorizontalOffset;
                    track.Thumb.RaiseEvent(new DragDeltaEventArgs(vertical ? 0 : 20, vertical ? 20 : 0) { RoutedEvent = Thumb.DragDeltaEvent });
                    await Idle(window);
                    if ((vertical ? scroller.VerticalOffset : scroller.HorizontalOffset) <= before) throw new Exception("Scrollbar drag does not advance content.");
                    var command = (RoutedCommand)track.IncreaseRepeatButton.Command;
                    before = vertical ? scroller.VerticalOffset : scroller.HorizontalOffset;
                    command.Execute(null, track.IncreaseRepeatButton); await Idle(window);
                    if ((vertical ? scroller.VerticalOffset : scroller.HorizontalOffset) <= before) throw new Exception("Scrollbar page command does not advance content.");
                }
                var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(Path.Combine(directory, theme + "-theme-controls.png")); encoder.Save(output);
                results.Add("PASS tab edge spacing and themed horizontal/vertical scrollbar drag/page: " + theme);
            }
        }
        finally { window.Close(); }
    }
    private static Task Idle(Window window) => window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ContextIdle).Task;
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is T item) yield return item; foreach (var nested in Descendants<T>(child)) yield return nested; }
    }
}
