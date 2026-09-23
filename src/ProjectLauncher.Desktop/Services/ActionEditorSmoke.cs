using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ProjectLauncher.Core;
using ProjectLauncher.Desktop.Controls;

namespace ProjectLauncher.Desktop.Services;

internal static class ActionEditorSmoke
{
    public static async Task RunAsync(string directory, List<string> results)
    {
        await Boundaries(directory, results);
        var project = Path.Combine(directory, "editor-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(project);
        var snapshot = ConfigStore.Save(Path.Combine(project, "launcher.yaml"), new LauncherConfig(), null);
        var window = new MainWindow(snapshot); window.Show();
        try
        {
            await Idle(window);
            var host = (ContentControl)window.FindName("PageHost");
            if (!Descendants<TextBlock>(window).Any(t => t.Text == "尚未添加启动动作")) throw new Exception("Missing empty state.");
            window.ShowPage("settings"); await Idle(window);
            var settings = window.SettingsPage;
            if (settings.HasUnsavedChanges) throw new Exception("Opening settings dirtied the draft.");
            var workspace = (ActionWorkspace)((ContentControl)settings.FindName("ActionWorkspaceHost")).Content;
            Click(workspace, "＋ 添加动作"); await Idle(window);
            Descendants<TextBox>(workspace).First(t => t.Text == "新的启动动作").Text = "启动服务";
            Descendants<TextBox>(workspace).First(t => !t.IsReadOnly && t.Text == "").Text = "service.py";
            Modal(workspace, "＋ 添加参数", dialog => {
                dialog.LabelBox.Text = "端口"; dialog.ArgumentBox.Text = "--port";
                dialog.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            await Idle(window);
            if (Descendants<ParameterForm>(workspace).Any()) throw new Exception("Draft preview still present.");
            if (!Descendants<TextBlock>(workspace).Any(t => t.Text == "端口")) throw new Exception("Parameter list missing saved field.");
            var addParameter = Descendants<Button>(workspace).Single(b => Equals(b.Content, "＋ 添加参数"));
            var parameterHeading = Descendants<TextBlock>(workspace).First(t => t.Text == "运行参数");
            if (Math.Abs(addParameter.TransformToAncestor(workspace).Transform(new Point()).Y - parameterHeading.TransformToAncestor(workspace).Transform(new Point()).Y) > 18)
                throw new Exception("Add parameter must be in the parameter heading row, not below the list.");
            var link = (System.Windows.Documents.Hyperlink)window.FindName("OfficialSiteLink");
            if (link.NavigateUri.AbsoluteUri != "https://github.com/JonGates/Python-Desktop-Launcher" ||
                new System.Windows.Documents.TextRange(link.ContentStart, link.ContentEnd).Text != "Python-Desktop-Launcher 官网")
                throw new Exception("Official site link mismatch.");
            foreach (var theme in new[] { "dark", "light" })
            {
                ThemeService.Apply(theme); window.Width = 1060; window.Height = 700; await Idle(window);
                Modal(workspace, "编辑", dialog => {
                    dialog.UpdateLayout();
                    var bottom = dialog.SaveButton.TransformToAncestor(dialog).Transform(new Point());
                    if (!dialog.SaveButton.IsVisible || bottom.Y + dialog.SaveButton.ActualHeight > dialog.ActualHeight) throw new Exception("Modal save button clipped.");
                    var render = new RenderTargetBitmap((int)dialog.ActualWidth, (int)dialog.ActualHeight, 96, 96, PixelFormats.Pbgra32); render.Render(dialog);
                    var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(render));
                    using var output = File.Create(Path.Combine(directory, theme + "-parameter-dialog.png")); png.Save(output);
                    dialog.Close();
                });
                var save = (Button)settings.FindName("SaveButton");
                var point = save.TransformToAncestor(window).Transform(new Point());
                if (!save.IsVisible || point.Y + save.ActualHeight > window.ActualHeight) throw new Exception("Save clipped at minimum window size.");
                var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(directory, theme + "-action-editor-compact.png")); encoder.Save(stream);
                results.Add("PASS compact native action editor: " + theme);
            }
            var id = workspace.SelectedActionId;
            ((Button)settings.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle(window);
            if (host.Content == settings || window.Shell.Config.Actions.Single().Id != id) throw new Exception("Save did not navigate to selected action.");
            if (!Descendants<TextBlock>(host).Any(t => t.Text.StartsWith("端口"))) throw new Exception("Saved parameter not visible on run page.");
            var run = (UserControl)host.Content;
            if (((Border)run.FindName("ParameterCard")).ActualHeight > 230) throw new Exception("Single parameter card retains excessive blank space.");
            var config = ConfigStore.Load(snapshot.Path).Config;
            if (config.Actions[0].Label != "启动服务" || config.Actions[0].Parameters!.Single() != config.Parameters.Single().Name) throw new Exception("Parameter not bound to action.");
            window.ShowPage("settings"); await Idle(window); settings = window.SettingsPage;
            workspace = (ActionWorkspace)((ContentControl)settings.FindName("ActionWorkspaceHost")).Content;
            Modal(workspace, "编辑", dialog => {
                dialog.TypeBox.SelectedItem = "integer";
                dialog.DefaultBox.Text = "invalid";
                dialog.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (!dialog.IsVisible || dialog.ErrorText.Text.Length == 0) throw new Exception("Invalid value accepted.");
                dialog.DefaultBox.Text = "0";
                dialog.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            ((Button)settings.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle(window);
            if (ValueCodec.Text(ConfigStore.Load(snapshot.Path).Config.Parameters[0].Default) != "0") throw new Exception("Zero default was lost.");
            results.Add("PASS empty project, unified editor, modal parameter list, official link, automatic binding, save navigation, invalid draft and zero default");
        }
        finally { window.SettingsPage.DiscardDirtyMarker(); window.Close(); await Idle(window); }
    }
    private static async Task Boundaries(string directory, List<string> results)
    {
        var failures = new List<string>();
        async Task Check(string name, Action<ActionWorkspace, LauncherConfig> check)
        {
            var config = new LauncherConfig { Actions = [new() { Id = "one", Parameters = ["port"] }, new() { Id = "two", Parameters = ["port"] }], Parameters = [new() { Name = "port", Label = "端口", Type = "integer", Argument = "--port", Default = 8000L }] };
            var workspace = new ActionWorkspace(config, directory, () => { });
            var w = new Window { Content = workspace, Width = 1060, Height = 700 }; w.Show(); await Idle(w);
            try { check(workspace, config); }
            catch (Exception ex) { failures.Add(name + ": " + ex.Message); }
            finally { w.Close(); }
        }
        await Check("shared scalar copy", (editor, config) => {
            Modal(editor, "编辑", dialog => dialog.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))); editor.Commit();
            var copy = config.Parameters.Single(p => p.Name != "port");
            if (ValueCodec.Text(copy.Default) != "8000" || config.Actions[1].Parameters!.Single() != "port") throw new Exception("Shared original changed.");
        });
        await Check("invalid switching", (editor, config) => {
            var timeout = Descendants<Expander>(editor).First(e => Equals(e.Header, "动作高级设置")); timeout.IsExpanded = true; editor.UpdateLayout();
            Descendants<TextBox>(timeout).First(t => !t.IsReadOnly).Text = "bad";
            Descendants<ComboBox>(editor).First(c => c.SelectedItem is ActionDefinition).SelectedIndex = 1;
            if (editor.SelectedActionId != "one" || !Descendants<TextBox>(timeout).Any(t => t.Text == "bad")) throw new Exception("Invalid draft lost on selection.");
        });
        await Check("invalid add parameter", (editor, config) => {
            var timeout = Descendants<Expander>(editor).First(e => Equals(e.Header, "动作高级设置")); timeout.IsExpanded = true; editor.UpdateLayout();
            Descendants<TextBox>(timeout).First(t => !t.IsReadOnly).Text = "bad";
            Click(editor, "＋ 添加参数");
            if (config.Parameters.Count != 1 || !Descendants<TextBox>(editor).Any(t => t.Text == "bad")) throw new Exception("Invalid input silently discarded.");
        });
        await Check("advanced empty argument", (editor, config) => {
            config.Actions[0].Argv = ["tool.exe", "--label", ""]; editor.SelectAction("one"); editor.Commit();
            if (config.Actions[0].Argv[2] != "") throw new Exception("Empty argv lost.");
        });
        await Check("cancel parameter dialog", (editor, config) => {
            var before = ConfigStore.Serialize(config);
            Modal(editor, "编辑", dialog => { dialog.LabelBox.Text = "取消的名称"; dialog.Close(); });
            if (ConfigStore.Serialize(config) != before) throw new Exception("Cancel mutated parameter.");
            Modal(editor, "＋ 添加参数", dialog => dialog.Close());
            if (ConfigStore.Serialize(config) != before) throw new Exception("Cancel added parameter.");
        });
        if (failures.Count != 0) throw new Exception(string.Join("\n", failures));
        results.Add("PASS shared scalar copying, invalid draft selection/addition guards, advanced empty argument preservation");
    }
    private static void Modal(ActionWorkspace workspace, string button, Action<ParameterEditorWindow> edit)
    {
        Exception? failure = null;
        workspace.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => {
            var dialog = Application.Current.Windows.OfType<ParameterEditorWindow>().SingleOrDefault();
            try { if (dialog is null) throw new Exception("Parameter dialog did not open."); edit(dialog); }
            catch (Exception ex) { failure = ex; }
            finally { if (dialog?.IsVisible == true) dialog.Close(); }
        }));
        Click(workspace, button);
        if (failure is not null) throw failure;
    }
    private static Task Idle(Window w) => w.Dispatcher.InvokeAsync(() => w.UpdateLayout(), DispatcherPriority.ContextIdle).Task;
    private static void Click(DependencyObject root, string text) => Descendants<Button>(root).First(b => Equals(b.Content, text)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is T t) yield return t; foreach (var nested in Descendants<T>(child)) yield return nested; }
    }
}
