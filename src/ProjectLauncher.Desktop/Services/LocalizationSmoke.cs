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
        var config = new LauncherConfig { App = new() { Name = "保存" }, Actions = [new() { Id = "one", Label = "运行项目", Parameters = [] }] };
        var snapshot = ConfigStore.Save(Path.Combine(root, "launcher.yaml"), config, null);
        var window = new MainWindow(snapshot); window.Show();
        try
        {
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
            LocalizationService.Current.SetLanguage("zh-CN");
            await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
            if (!Equals(((Button)settings.FindName("SaveButton")).Content, "保存并查看")) throw new Exception("Chinese restoration failed.");
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
