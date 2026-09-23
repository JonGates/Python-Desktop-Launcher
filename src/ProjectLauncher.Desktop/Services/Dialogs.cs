using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ProjectLauncher.Desktop.Services;

public static class Dialogs
{
    public static bool Confirm(Window? owner, string title, string message, string accept = "", bool danger = false)
    {
        if (accept.Length == 0) accept = L.Text("Text.109");
        var window = Create(owner, title, 590);
        var root = new StackPanel { Margin = new Thickness(28) };
        root.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.FindResource("SectionTitle"), FontSize = 21 });
        var body = new TextBox { Text = message, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
            Padding = new Thickness(0), Margin = new Thickness(0, 16, 0, 22), MaxHeight = 390, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        body.SetResourceReference(Control.ForegroundProperty, "SecondaryText"); root.Children.Add(body);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "", MinWidth = 90, IsCancel = true, Margin = new Thickness(0, 0, 10, 0) }.Localize(ContentControl.ContentProperty, "Ui.046");
        var ok = new Button { Content = accept, MinWidth = 110, IsDefault = true, Style = (Style)Application.Current.FindResource(danger ? "DangerButton" : "PrimaryButton") };
        ok.Click += (_, _) => window.DialogResult = true;
        buttons.Children.Add(cancel); buttons.Children.Add(ok); root.Children.Add(buttons); window.Content = root;
        return window.ShowDialog() == true;
    }
    public static void Info(Window? owner, string title, string message)
    {
        var window = Create(owner, title, 650);
        var grid = new Grid { Margin = new Thickness(26) }; grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.FindResource("SectionTitle") });
        var text = new TextBox { Text = message, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 420, MinHeight = 120, Margin = new Thickness(0, 16, 0, 18) };
        Grid.SetRow(text, 1); grid.Children.Add(text);
        var button = new Button { Content = "", IsCancel = true, IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 100 }.Localize(ContentControl.ContentProperty, "Ui.004");
        button.Click += (_, _) => window.Close(); Grid.SetRow(button, 2); grid.Children.Add(button); window.Content = grid; window.ShowDialog();
    }
    public static string? Input(Window? owner, string title, string description, string value = "")
    {
        var window = Create(owner, title, 520); var root = new StackPanel { Margin = new Thickness(26) };
        root.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.FindResource("SectionTitle") });
        root.Children.Add(new TextBlock { Text = description, Style = (Style)Application.Current.FindResource("Hint"), Margin = new Thickness(0, 8, 0, 14) });
        var input = new TextBox { Text = value, Margin = new Thickness(0, 0, 0, 18) }; root.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(new Button { Content = "", IsCancel = true, Margin = new Thickness(0, 0, 10, 0) }.Localize(ContentControl.ContentProperty, "Ui.046"));
        var ok = new Button { Content = "", IsDefault = true, Style = (Style)Application.Current.FindResource("PrimaryButton") }.Localize(ContentControl.ContentProperty, "Text.111");
        ok.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(input.Text)) window.DialogResult = true; };
        buttons.Children.Add(ok); root.Children.Add(buttons); window.Content = root; window.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        return window.ShowDialog() == true ? input.Text.Trim() : null;
    }
    private static Window Create(Window? owner, string title, double width)
    {
        var w = new Window { Title = title, Width = width, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = owner is null,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner, MaxHeight = SystemParameters.WorkArea.Height - 40 };
        if (owner is not null && owner.IsVisible) w.Owner = owner; return w;
    }
}
