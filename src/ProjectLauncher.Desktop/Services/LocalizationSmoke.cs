using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ProjectLauncher.Core;
using ProjectLauncher.Desktop.Controls;

namespace ProjectLauncher.Desktop.Services;

internal static class LocalizationSmoke
{
    public static async Task RunAsync(string directory, List<string> results)
    {
        var root = Path.Combine(directory, "language-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var config = new LauncherConfig {
            App = new() { Name = "保存" },
            Actions = [new() { Id = "one", Label = "运行项目", Parameters = ["count", "value"] }],
            Parameters = [new() { Name = "count", Type = "integer", Argument = "--count" },
                new() { Name = "value", Type = "text", Argument = "--value" }]
        };
        var snapshot = ConfigStore.Save(Path.Combine(root, "launcher.yaml"), config, null);
        var window = new MainWindow(snapshot); window.Show();
        try
        {
            var failures = new List<string>();
            foreach (var language in new[] { "zh-CN", "en-US" })
            foreach (var theme in new[] { "light", "dark" })
            {
                LocalizationService.Current.SetLanguage(language);
                ThemeService.Apply(theme);
                _ = window.Dispatcher.BeginInvoke(new Action(() => {
                    var dialog = window.OwnedWindows.Cast<Window>().Single();
                    try
                    {
                        var version = typeof(MainWindow).Assembly.GetName().Version!.ToString(3);
                        if (!dialog.Title.Contains("Python Desktop Launcher") || !dialog.Title.Contains(version)
                            || dialog.Title.Contains("Preview")) failures.Add("About title does not match the current product/version.");
                        dialog.UpdateLayout();
                        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)dialog.ActualWidth, (int)dialog.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                        bitmap.Render(dialog);
                        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                        using var stream = File.Create(Path.Combine(directory, $"about-{language}-{theme}.png"));
                        encoder.Save(stream);
                    }
                    finally { dialog.Close(); }
                }), DispatcherPriority.ApplicationIdle);
                Descendants<Button>(window).Single(b => Equals(b.Content, "?"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            LocalizationService.Current.SetLanguage("zh-CN");
            window.ShowPage("run");
            await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
            var run = (Views.RunView)((ContentControl)window.FindName("PageHost")).Content;
            var form = (ParameterForm)run.FindName("Form");
            var inputs = Descendants<TextBox>(form).ToArray();
            inputs[0].Text = "invalid-number"; inputs[1].Text = "";
            var previewHint = (TextBlock)run.FindName("PreviewHint");
            var cachedDiagnostic = previewHint.GetBindingExpression(TextBlock.TextProperty);
            window.ShowPage("settings"); window.SettingsPage.SelectDesigner();
            await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
            var settings = window.SettingsPage;
            var name = (TextBox)settings.FindName("NameBox"); name.Text = "未保存";
            var workspace = ((ContentControl)settings.FindName("ActionWorkspaceHost")).Content;
            var editor = (ActionWorkspace)workspace;
            var advanced = Descendants<Expander>(editor).Single(); advanced.IsExpanded = true;
            await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
            var timeout = Descendants<TextBox>(advanced).First(t => !t.IsReadOnly); timeout.Text = "invalid-draft";
            var selectedKind = Descendants<ComboBox>(editor).First().SelectedItem;
            var before = File.ReadAllText(snapshot.Path);
            LocalizationService.Current.SetLanguage("en-US");
            await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
            if (!ReferenceEquals(settings, window.SettingsPage) || !ReferenceEquals(workspace, ((ContentControl)settings.FindName("ActionWorkspaceHost")).Content)) throw new Exception("Language change rebuilt views.");
            if (name.Text != "未保存" || !settings.HasUnsavedChanges || window.Shell.ProjectName != "保存") throw new Exception("Language change mutated user state.");
            if (timeout.Text != "invalid-draft" || !Equals(Descendants<ComboBox>(editor).First().SelectedItem, selectedKind)) throw new Exception("Language change changed an invalid input or launch type.");
            if (!Descendants<Button>(editor).Any(b => Equals(b.Content, "＋ Add parameter"))) throw new Exception("Programmatic editor labels did not translate live.");
            if (File.ReadAllText(snapshot.Path) != before) throw new Exception("Language wrote project config.");
            if (!Equals(((Button)settings.FindName("SaveButton")).Content, "Save and view")) throw new Exception("Save button did not translate live.");
            if (!ReferenceEquals(cachedDiagnostic, previewHint.GetBindingExpression(TextBlock.TextProperty)))
                failures.Add("Language switch rebuilt command preview and reparsed business inputs.");
            if (inputs[0].Text != "invalid-number" || inputs[1].Text != "")
                failures.Add("Language switch changed invalid numeric or empty run inputs.");
            LocalizationService.Current.SetLanguage("zh-CN");
            await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
            if (!Equals(((Button)settings.FindName("SaveButton")).Content, "保存并查看")) throw new Exception("Chinese restoration failed.");
            window.ShowPage("terminal");
            await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
            Descendants<Button>(window.TerminalPage).Single(b => Equals(b.Content, L.Text("Ui.106")))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            LocalizationService.Current.SetLanguage("en-US");
            if (window.Shell.NotificationDetail != L.Text("Text.207"))
                failures.Add("Visible terminal error toast did not switch language.");
            if (failures.Count != 0) throw new Exception(string.Join("\n", failures));
            results.Add("PASS live language switch preserves controls, draft, user labels and config");
        }
        finally { LocalizationService.Current.SetLanguage("zh-CN"); window.SettingsPage.DiscardDirtyMarker(); window.Close(); }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++) {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T item) yield return item;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
