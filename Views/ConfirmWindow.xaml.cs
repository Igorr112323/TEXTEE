using System.Windows;

namespace Visits11.Views;

public enum ConfirmChoice
{
    Cancel,
    Ok,
    Extra,
}

public partial class ConfirmWindow : Window
{
    public ConfirmChoice Choice { get; private set; } = ConfirmChoice.Cancel;

    public ConfirmWindow()
    {
        InitializeComponent();
    }

    public static ConfirmChoice Show(string title, string message, string ok, string? extra = null, bool topmost = false)
    {
        var window = new ConfirmWindow { Topmost = topmost };
        window.TitleText.Text = title;
        window.MessageText.Text = message;
        window.OkButton.Content = ok;
        if (Application.Current?.MainWindow is { IsVisible: true } owner) window.Owner = owner;
        if (extra is null)
        {
            window.ExtraButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            window.ExtraButton.Content = extra;
        }
        window.ShowDialog();
        return window.Choice;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Choice = ConfirmChoice.Ok;
        DialogResult = true;
    }

    private void Extra_Click(object sender, RoutedEventArgs e)
    {
        Choice = ConfirmChoice.Extra;
        DialogResult = true;
    }
}
