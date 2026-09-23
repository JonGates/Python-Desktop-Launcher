using System.Windows;
using System.Windows.Controls;
using ProjectLauncher.Desktop.Services;
using ProjectLauncher.Desktop.ViewModels;

namespace ProjectLauncher.Desktop.Controls;

public partial class ToastHost : UserControl
{
    public ToastHost() => InitializeComponent();
    private void Details_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel shell) Dialogs.Info(Window.GetWindow(this), L.Text("Text.018"), shell.NotificationDetail);
    }
    private void Dismiss_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel shell) shell.DismissNotification();
    }
}
