using System.Windows;
using System.Windows.Controls;
using Visits11.ViewModels;

namespace Visits11.Views;

public partial class SpotCheckWindow : Window
{
    public SpotCheckWindow(IReadOnlyList<StudentRow> rows, Action<StudentRow> absent)
    {
        InitializeComponent();
        foreach (var row in rows)
        {
            var line = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new TextBlock
            {
                Text = row.FullName,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Style = (Style)FindResource("Txt.Body"),
            };
            var button = new Button
            {
                Content = "Не был",
                Style = (Style)FindResource("Btn.Ghost"),
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(12, 0, 0, 0),
            };
            var captured = row;
            button.Click += (_, _) =>
            {
                absent(captured);
                button.IsEnabled = false;
                button.Content = "Снят";
            };
            Grid.SetColumn(button, 1);
            line.Children.Add(name);
            line.Children.Add(button);
            List.Children.Add(line);
        }
    }

    public static void Show(IReadOnlyList<StudentRow> rows, Action<StudentRow> absent)
    {
        var window = new SpotCheckWindow(rows, absent)
        {
            Owner = Application.Current?.MainWindow,
        };
        window.ShowDialog();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
