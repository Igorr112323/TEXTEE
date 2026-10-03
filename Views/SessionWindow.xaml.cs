using System.Windows;
using System.Windows.Input;

namespace Visits11.Views;

public partial class SessionWindow : Window
{
    public SessionWindow()
    {
        InitializeComponent();
    }

    private void Window_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 1) return;
        try { DragMove(); } catch { /* окно неактивно */ }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
