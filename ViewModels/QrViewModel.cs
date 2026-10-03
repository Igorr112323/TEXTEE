using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Visits11.Services;
using Visits11.Views;

namespace Visits11.ViewModels;

public sealed class QrViewModel : ObservableObject, ITabViewModel
{
    private readonly AppSettings _settings;
    private readonly ToastService _toasts;
    private readonly BackupService _backups;
    private readonly MainViewModel _main;
    private readonly PortalServer _server = new();
    private readonly ReminderService _reminder;
    private readonly DispatcherTimer _timer;
    private bool _loading;
    private string _wifiPayload = string.Empty;
    private string _urlPayload = string.Empty;

    public QrViewModel(AppSettings settings, ToastService toasts, BackupService backups, MainViewModel main)
    {
        _settings = settings;
        _toasts = toasts;
        _backups = backups;
        _main = main;
        _reminder = new ReminderService(settings);
        _reminder.Fired += OnReminder;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _timer.Tick += (_, _) => RefreshQr();

        _loading = true;
        TeacherName = settings.TeacherName;
        WifiSsid = settings.WifiSsid;
        WifiPassword = settings.WifiPassword;
        PortText = settings.Port.ToString();
        SuddenFromText = settings.SuddenFrom.ToString();
        SuddenToText = settings.SuddenTo.ToString();
        _loading = false;
        LastBackupText = backups.LastText;

        ProjectorCommand = new RelayCommand(_ => ProjectorWindow.Show(this));
        AllowCommand = new RelayCommand(_ => AllowPhones());
        OpenSiteCommand = new RelayCommand(_ => OpenSite());
        RestartCommand = new RelayCommand(_ => Restart());
        ChangePinCommand = new RelayCommand(_ => ChangePin());
        BackupCommand = new RelayCommand(_ => MakeBackup());
        ArmCommand = new RelayCommand(_ => Arm());
    }

    public RelayCommand ProjectorCommand { get; }
    public RelayCommand AllowCommand { get; }
    public RelayCommand OpenSiteCommand { get; }
    public RelayCommand RestartCommand { get; }
    public RelayCommand ChangePinCommand { get; }
    public RelayCommand BackupCommand { get; }
    public RelayCommand ArmCommand { get; }

    private string _teacherName = string.Empty;
    public string TeacherName
    {
        get => _teacherName;
        set
        {
            if (Set(ref _teacherName, value)) Persist(restart: false);
        }
    }

    private string _wifiSsid = string.Empty;
    public string WifiSsid
    {
        get => _wifiSsid;
        set
        {
            if (Set(ref _wifiSsid, value)) Persist(restart: false);
        }
    }

    private string _wifiPassword = string.Empty;
    public string WifiPassword
    {
        get => _wifiPassword;
        set
        {
            if (Set(ref _wifiPassword, value)) Persist(restart: false);
        }
    }

    private string _portText = "8090";
    public string PortText
    {
        get => _portText;
        set
        {
            if (!Set(ref _portText, value)) return;
            if (_loading) return;
            if (int.TryParse(value, out var port) && port is >= 1024 and <= 65535 && port != _settings.Port)
            {
                _settings.Port = port;
                Persist(restart: true);
            }
        }
    }

    private string _pinText = string.Empty;
    public string PinText
    {
        get => _pinText;
        set => Set(ref _pinText, value);
    }

    private string _suddenFromText = "15";
    public string SuddenFromText
    {
        get => _suddenFromText;
        set => Set(ref _suddenFromText, value);
    }

    private string _suddenToText = "60";
    public string SuddenToText
    {
        get => _suddenToText;
        set => Set(ref _suddenToText, value);
    }

    private string _serverStatus = "Сервер ещё не запущен";
    private string _statusText = "Сервер ещё не запущен";
    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    private string _urlText = string.Empty;
    public string UrlText
    {
        get => _urlText;
        private set => Set(ref _urlText, value);
    }

    private string _ssidText = "Имя сети не задано";
    public string SsidText
    {
        get => _ssidText;
        private set => Set(ref _ssidText, value);
    }

    private string _suddenStatus = "Сигнал выключен. Момент, если включить, студентам не показывается.";
    public string SuddenStatus
    {
        get => _suddenStatus;
        private set => Set(ref _suddenStatus, value);
    }

    private string _lastBackupText = string.Empty;
    public string LastBackupText
    {
        get => _lastBackupText;
        private set => Set(ref _lastBackupText, value);
    }

    private ImageSource? _wifiQr;
    public ImageSource? WifiQr
    {
        get => _wifiQr;
        private set => Set(ref _wifiQr, value);
    }

    private ImageSource? _urlQr;
    public ImageSource? UrlQr
    {
        get => _urlQr;
        private set => Set(ref _urlQr, value);
    }

    public bool HasWifiQr => WifiQr is not null;
    public bool HasUrlQr => UrlQr is not null;

    public void Start()
    {
        _reminder.Start();
        Restart();
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        _reminder.Stop();
        _server.Stop();
    }

    public void OnActivated() => RefreshQr();

    private void Persist(bool restart)
    {
        if (_loading) return;
        _settings.TeacherName = TeacherName.Trim();
        _settings.WifiSsid = WifiSsid.Trim();
        _settings.WifiPassword = WifiPassword;
        _server.TeacherName = _settings.TeacherName;
        _server.Ssid = _settings.WifiSsid;
        _settings.Save();
        if (restart && _server.IsRunning) Restart();
        else RefreshQr();
    }

    private void Restart()
    {
        var result = _server.Start(_settings.Port);
        if (result.Ok && result.Port != _settings.Port)
        {
            _settings.Port = result.Port;
            _settings.Save();
            _loading = true;
            PortText = result.Port.ToString();
            _loading = false;
        }
        _server.TeacherName = TeacherName.Trim();
        _server.Ssid = WifiSsid.Trim();
        _serverStatus = !result.Ok
            ? result.Error ?? "Сервер не запущен"
            : result.Lan
                ? $"Сервер открыт для телефонов, порт {result.Port}"
                : result.Error ?? "Сервер только на этом компьютере";
        StatusText = _serverStatus;
        RefreshQr();
    }

    private void RefreshQr()
    {
        var ssid = WifiSsid.Trim();
        var password = WifiPassword;
        var passwordOk = password.Length == 0 || password.Length is >= 8 and <= 63;
        SsidText = string.IsNullOrEmpty(ssid) ? "Имя сети не задано" : ssid;
        if (!string.IsNullOrEmpty(ssid) && passwordOk)
        {
            var payload = PortalServer.WifiPayload(ssid, password);
            if (payload != _wifiPayload)
            {
                _wifiPayload = payload;
                WifiQr = QrImages.FromText(payload);
                OnPropertyChanged(nameof(HasWifiQr));
            }
            if (StatusText.StartsWith("Пароль", StringComparison.Ordinal)) StatusText = _serverStatus;
        }
        else
        {
            _wifiPayload = string.Empty;
            if (WifiQr is not null)
            {
                WifiQr = null;
                OnPropertyChanged(nameof(HasWifiQr));
            }
            if (!string.IsNullOrEmpty(ssid) && !passwordOk)
                StatusText = "Пароль сети: пустой или от 8 до 63 символов";
            else
                StatusText = _serverStatus;
        }

        var ip = PortalServer.LocalIp();
        var port = _server.IsRunning ? _server.Port : _settings.Port;
        var url = ip is null ? $"http://127.0.0.1:{port}/" : $"http://{ip}:{port}/";
        UrlText = ip is null
            ? "Компьютер не в сети. Включите точку доступа Windows — адрес обычно 192.168.137.1"
            : url;
        if (url != _urlPayload)
        {
            _urlPayload = url;
            UrlQr = QrImages.FromText(url);
            OnPropertyChanged(nameof(HasUrlQr));
        }
    }

    private void OpenSite()
    {
        var url = string.IsNullOrEmpty(_urlPayload) ? $"http://127.0.0.1:{_settings.Port}/" : _urlPayload;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            _toasts.Error("Не удалось открыть сайт", exception.Message);
        }
    }

    private void AllowPhones()
    {
        var port = _server.IsRunning ? _server.Port : _settings.Port;
        StatusText = "Ждём разрешение Windows…";
        Task.Run(() =>
        {
            try
            {
                var args = "-NoProfile -ExecutionPolicy Bypass -Command \"" +
                           $"netsh http add urlacl url=http://+:{port}/ user=Everyone; " +
                           $"netsh advfirewall firewall add rule name=KubGAU{port} dir=in action=allow protocol=TCP localport={port}\"";
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = args,
                    Verb = "runas",
                    UseShellExecute = true,
                });
                process?.WaitForExit();
            }
            catch
            {
            }
            Application.Current?.Dispatcher.Invoke(() =>
            {
                Restart();
                _toasts.Info("Порт", StatusText);
            });
        });
    }

    private void ChangePin()
    {
        var pin = PinText.Trim();
        if (pin.Length != 4 || pin.Any(ch => !char.IsDigit(ch)))
        {
            _toasts.Error("ПИН", "Нужны ровно 4 цифры.");
            return;
        }
        _settings.SetPin(pin);
        _settings.Save();
        PinText = string.Empty;
        _toasts.Success("ПИН изменён", "Новый код спросит при следующем запуске.");
    }

    private void MakeBackup()
    {
        try
        {
            _backups.Snapshot();
            LastBackupText = _backups.LastText;
            _toasts.Success("Копия создана", _backups.LastText);
        }
        catch (Exception exception)
        {
            _toasts.Error("Копия не создана", exception.Message);
        }
    }

    private void Arm()
    {
        if (!int.TryParse(SuddenFromText, out var from) || !int.TryParse(SuddenToText, out var to))
        {
            _toasts.Error("Минуты", "Укажите числа от 1 до 180.");
            return;
        }
        if (from > to) (from, to) = (to, from);
        _reminder.Arm(from, to);
        SuddenFromText = from.ToString();
        SuddenToText = to.ToString();
        SuddenStatus = $"Сигнал включён. Момент скрыт и придёт один раз между {from} и {to} минутой.";
        _toasts.Info("Внезапная перекличка", "Момент скрыт. Отметку всё равно ставит камера.");
    }

    private void OnReminder()
    {
        Application.Current?.MainWindow?.Activate();
        _main.TabLessons = true;
        SuddenStatus = "Сигнал уже прозвучал. Чтобы повторить, включите ещё раз.";
        ConfirmWindow.Show(
            "Внезапная перекличка",
            "Пора отметить студентов камерой. Выберите группу и нажмите «Начать перекличку». Сеть для отметки не нужна.",
            "К занятиям",
            topmost: true);
    }
}
