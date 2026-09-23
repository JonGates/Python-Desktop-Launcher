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
        EnvironmentBox.Items.Add(new EnvironmentChoice("稍后在环境管理页创建或设置", null));
        DiscoveryStatus.Text = environments.Count switch
        {
            0 => "未发现完整的虚拟环境。可以选择已有环境目录，或先生成配置。",
            1 => "发现 1 个虚拟环境，已为你选中。请确认目录。",
            _ => $"发现 {environments.Count} 个虚拟环境，请选择此项目使用的环境。"
        };
        EnvironmentBox.SelectedIndex = environments.Count <= 1 ? 0 : -1;
    }

    private void Environment_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (EnvironmentPath is null) return;
        EnvironmentPath.Text = (EnvironmentBox.SelectedItem as EnvironmentChoice)?.Directory ?? "配置可先保存，运行任务前需准备项目环境。";
    }

    private void BrowseEnvironment_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "选择虚拟环境根目录（含 pyvenv.cfg）", InitialDirectory = _project };
        if (picker.ShowDialog(this) != true) return;
        if (!ProjectSetup.IsEnvironment(picker.FolderName))
        {
            ErrorText.Text = "此目录缺少 pyvenv.cfg 或环境内的 Python 解释器。请选择虚拟环境根目录，而不是 Scripts 文件夹。"; return;
        }
        var choice = new EnvironmentChoice(picker.FolderName, picker.FolderName);
        EnvironmentBox.Items.Add(choice); EnvironmentBox.SelectedItem = choice; ErrorText.Text = "";
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (EnvironmentBox.SelectedItem is not EnvironmentChoice choice)
        {
            ErrorText.Text = "请选择一个环境，或选择稍后设置。"; return;
        }
        try
        {
            Snapshot = ProjectSetup.Create(_configPath, choice.Directory, null);
            DialogResult = true;
        }
        catch (Exception ex) { ErrorText.Text = ex.Message; }
    }
}
