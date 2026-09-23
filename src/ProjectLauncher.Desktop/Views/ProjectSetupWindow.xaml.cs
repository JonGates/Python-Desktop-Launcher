using ProjectLauncher.Desktop.Services;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ProjectLauncher.Core;

namespace ProjectLauncher.Desktop.Views;

public partial class ProjectSetupWindow : Window
{
    private readonly string _configPath;
    private readonly string _project;
    public ConfigSnapshot? Snapshot { get; private set; }
    private sealed record EnvironmentChoice(string Label, string? Directory);

    public ProjectSetupWindow(string configPath)
    {
        _configPath = Path.GetFullPath(configPath); _project = Path.GetDirectoryName(_configPath)!;
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height - 32;
        Height = Math.Min(Height, MaxHeight);
        ProjectPath.Text = _project;
        var environments = ProjectSetup.FindEnvironments(_project);
        foreach (var directory in environments)
            EnvironmentBox.Items.Add(new EnvironmentChoice(Path.GetRelativePath(_project, directory), directory));
        EnvironmentBox.Items.Add(new EnvironmentChoice(L.Text("Text.158"), null));
        DiscoveryStatus.Text = environments.Count switch
        {
            0 => L.Text("Text.159"),
            1 => L.Text("Text.160"),
            _ => L.Text("Setup.Discovered", environments.Count)
        };
        EnvironmentBox.SelectedIndex = environments.Count <= 1 ? 0 : -1;
    }

    private void Environment_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (EnvironmentPath is null) return;
        EnvironmentPath.Text = (EnvironmentBox.SelectedItem as EnvironmentChoice)?.Directory ?? L.Text("Text.161");
    }

    private void BrowseEnvironment_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = L.Text("Text.162"), InitialDirectory = _project };
        if (picker.ShowDialog(this) != true) return;
        if (!ProjectSetup.IsEnvironment(picker.FolderName))
        {
            ErrorText.Text = L.Text("Text.163"); return;
        }
        var choice = new EnvironmentChoice(picker.FolderName, picker.FolderName);
        EnvironmentBox.Items.Add(choice); EnvironmentBox.SelectedItem = choice; ErrorText.Text = "";
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (EnvironmentBox.SelectedItem is not EnvironmentChoice choice)
        {
            ErrorText.Text = L.Text("Text.164"); return;
        }
        try
        {
            Snapshot = ProjectSetup.Create(_configPath, choice.Directory, null);
            DialogResult = true;
        }
        catch (Exception ex) { ErrorText.Text = ex.Message; }
    }
}
