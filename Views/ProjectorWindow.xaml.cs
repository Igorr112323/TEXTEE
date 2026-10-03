using System.Windows;
using System.Windows.Input;
using Visits11.ViewModels;

namespace Visits11.Views;

public partial class ProjectorWindow : Window
{
    public ProjectorWindow()
    {
        InitializeComponent();
    }

    public static void Show(QrViewModel viewModel)
    {
        var open = Application.Current.Windows.OfType<ProjectorWindow>().FirstOrDefault();
        if (open is not null)
        {
            open.Activate();
            return;
        }
        var window = new ProjectorWindow
        {
            DataContext = viewModel,
            Owner = Application.Current?.MainWindow,
        };
        window.Show();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }
}
