using System.IO;
using System.Windows;
using Visits11.Services;
using Visits11.ViewModels;
using Visits11.Views;

namespace Visits11;

public partial class App : Application
{
    private MainViewModel? _viewModel;

    public static DatabaseService Db { get; private set; } = null!;
    public static ThemeService Theme { get; private set; } = null!;
    public static ToastService Toasts { get; private set; } = null!;
    public static AppSettings Settings { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var appDir = GetAppDirectory();
        var dbPath = Path.Combine(appDir, "attendance.db");
        Db = new DatabaseService(dbPath);
        Db.Initialize();
        Settings = new AppSettings(Path.Combine(appDir, "settings.json"));
        Settings.Load();
        Theme = new ThemeService(Settings);
        Theme.LoadAndApply();
        Toasts = new ToastService();
        var backups = new BackupService(appDir, dbPath);
        try { backups.EnsureDaily(); } catch { }
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        if (!PinWindow.Check(Settings))
        {
            Shutdown();
            return;
        }
        var viewModel = new MainViewModel(Db, Toasts, Theme, new AuthService(), new WordService(), Settings, backups);
        _viewModel = viewModel;
        var window = new MainWindow(viewModel);
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();
        viewModel.Qr.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _viewModel?.Qr.Stop();
        base.OnExit(e);
    }

    private static string GetAppDirectory()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath))
        {
            var dir = Path.GetDirectoryName(processPath);
            if (!string.IsNullOrEmpty(dir)) return dir;
        }
        return AppContext.BaseDirectory;
    }
}
