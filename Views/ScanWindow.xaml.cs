using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Visits11.Services;

namespace Visits11.Views;

public partial class ScanWindow : Window
{
    private readonly string _rollId;
    private readonly QrProtocol.Collector _collector = new();
    private WebcamCapture? _camera;
    private int _decodeBusy;
    private long _lastPreviewAt;
    private bool _done;

    public event Action<string>? ResultReady;

    public ScanWindow(string rollId)
    {
        _rollId = rollId;
        InitializeComponent();
        Loaded += (_, _) => StartCamera();
        Closed += (_, _) => _camera?.Dispose();
    }

    private void StartCamera()
    {
        try
        {
            _camera = new WebcamCapture();
            _camera.FrameJpeg += OnFrame;
            if (!_camera.Start())
            {
                Status.Text = "Камера ПК не найдена. Сохраните файл на телефоне и нажмите «Импорт файла».";
            }
        }
        catch (Exception ex)
        {
            Status.Text = "Камера ПК недоступна. Сохраните файл на телефоне и импортируйте его. " + ex.Message;
        }
    }

    private void OnFrame(byte[] jpeg)
    {
        var now = Environment.TickCount64;
        if (now - _lastPreviewAt > 120)
        {
            _lastPreviewAt = now;
            Dispatcher.BeginInvoke(() => ShowPreview(jpeg));
        }
        if (Interlocked.Exchange(ref _decodeBusy, 1) == 1) return;
        var text = QrScanner.ScanJpeg(jpeg);
        Interlocked.Exchange(ref _decodeBusy, 0);
        if (string.IsNullOrEmpty(text)) return;
        Dispatcher.BeginInvoke(() => Accept(text));
    }

    private void ShowPreview(byte[] jpeg)
    {
        try
        {
            var image = new BitmapImage();
            using var stream = new MemoryStream(jpeg);
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            Preview.Source = image;
        }
        catch
        {
            // кадр битый
        }
    }

    private void Accept(string text)
    {
        if (_done) return;
        if (!QrProtocol.TryParseFrame(text, out var frame) || frame.Kind != 'R')
        {
            Status.Text = "Это не итог переклички. На телефоне нажмите «Передать на ПК».";
            return;
        }
        if (!string.Equals(frame.RollId, _rollId, StringComparison.OrdinalIgnoreCase))
        {
            Status.Text = "Это итог другой переклички.";
            return;
        }
        if (_collector.Add(frame, out var body) && body is not null)
        {
            _done = true;
            Status.Text = "Итог считан.";
            ResultReady?.Invoke(body);
            Close();
            return;
        }
        Status.Text = _collector.Count <= 1
            ? "Считываю код…"
            : $"Получено {_collector.Got} из {_collector.Count}. Держите телефон перед камерой.";
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Итог с телефона преподавателя",
            Filter = "Итог переклички (*.kjournal)|*.kjournal|Все файлы|*.*",
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var lines = File.ReadAllLines(dialog.FileName);
            foreach (var line in lines)
            {
                if (line.Length > 0) Accept(line);
                if (_done) return;
            }
            Status.Text = "В файле не хватает кадров итога.";
        }
        catch (Exception ex)
        {
            Status.Text = ex.Message;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }

    private void Title_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try { DragMove(); } catch { /* окно неактивно */ }
    }
}
