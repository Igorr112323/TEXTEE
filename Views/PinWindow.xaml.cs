using System.Windows;
using Visits11.Services;

namespace Visits11.Views;

public partial class PinWindow : Window
{
    private readonly AppSettings _settings;

    public PinWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        Hint.Visibility = settings.IsDefaultPin ? Visibility.Visible : Visibility.Collapsed;
        PinBox.Focus();
    }

    public static bool Check(AppSettings settings)
    {
        var window = new PinWindow(settings);
        return window.ShowDialog() == true;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.CheckPin(PinBox.Text))
        {
            DialogResult = true;
            return;
        }
        PinBox.Text = string.Empty;
        Hint.Text = "Неверный ПИН.";
        Hint.Visibility = Visibility.Visible;
    }
}
