using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Visits11.Models;

namespace Visits11.Views;

public partial class SummaryWindow : Window
{
    private readonly Action _save;

    public SummaryWindow(SemesterReport report, Action save)
    {
        _save = save;
        InitializeComponent();
        TitleText.Text = string.IsNullOrEmpty(report.Title) ? "Сводка семестра" : report.Title;
        Grid.Columns.Add(TextColumn("ФИО", "Name", 220));
        for (var i = 0; i < report.Headers.Count; i++)
        {
            Grid.Columns.Add(TextColumn(report.Headers[i], $"Marks[{i}]", 64));
        }
        Grid.Columns.Add(TextColumn("Итого", "TotalText", 72));
        Grid.Columns.Add(TextColumn("%", "PercentText", 56));
        Grid.ItemsSource = report.Students;
    }

    public static void Show(SemesterReport report, Action save)
    {
        var window = new SummaryWindow(report, save)
        {
            Owner = Application.Current?.MainWindow,
        };
        window.ShowDialog();
    }

    private static DataGridTextColumn TextColumn(string header, string path, double width)
    {
        return new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(path),
            Width = width,
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e) => _save();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Title_OnMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) DragMove();
    }
}
