using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Visits11.Models;
using Visits11.Services;
using Visits11.Views;

namespace Visits11.ViewModels;

public sealed class StudentRow : ObservableObject
{
    public int StudentId { get; init; }
    public int Number { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string? PhoneId { get; set; }

    private bool _isPresent;
    public bool IsPresent
    {
        get => _isPresent;
        set
        {
            if (Set(ref _isPresent, value))
            {
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public string StatusText => IsPresent ? "Есть" : "Нет";

    private bool _suspicious;
    public bool Suspicious
    {
        get => _suspicious;
        set => Set(ref _suspicious, value);
    }

    public string? MarkedAt { get; set; }
}

public sealed class LessonViewModel : ObservableObject, ITabViewModel
{
    private const string StateIdle = "Idle";
    private const string StateActive = "Active";
    private const string StateFinished = "Finished";

    private readonly DatabaseService _database;
    private readonly ToastService _toasts;

    private DispatcherTimer? _resetTimer;
    private DispatcherTimer? _cycleTimer;
    private string _lessonStartedAt = string.Empty;
    private string _rollId = string.Empty;
    private List<string> _sessionFrames = new();
    private int _sessionFrameIndex;
    private SessionWindow? _sessionWindow;

    public LessonViewModel(DatabaseService database, ToastService toasts)
    {
        _database = database;
        _toasts = toasts;
        StartStopCommand = new RelayCommand(_ => StartOrStop(), _ => RollcallState != StateFinished);
        TogglePresentCommand = new RelayCommand(row => TogglePresent((StudentRow)row!), _ => RollcallState == StateActive);
        ShowSessionCommand = new RelayCommand(_ => ShowSessionWindow(), _ => RollcallState == StateActive);
        ScanResultCommand = new RelayCommand(_ => OpenScanner(), _ => RollcallState == StateActive);
        ImportResultCommand = new RelayCommand(_ => ImportFile(), _ => RollcallState == StateActive);
        SpotCheckCommand = new RelayCommand(_ => ShowSpotCheck(), _ => RollcallState == StateActive);
        _database.DataChanged += OnDataChanged;
        ReloadGroups();
    }

    public ObservableCollection<Group> Groups { get; } = new();
    public ObservableCollection<StudentRow> Rows { get; } = new();

    public RelayCommand StartStopCommand { get; }
    public RelayCommand TogglePresentCommand { get; }
    public RelayCommand ShowSessionCommand { get; }
    public RelayCommand ScanResultCommand { get; }
    public RelayCommand ImportResultCommand { get; }
    public RelayCommand SpotCheckCommand { get; }

    private Group? _selectedGroup;
    public Group? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (Set(ref _selectedGroup, value) && RollcallState != StateActive)
            {
                LoadStudents();
            }
        }
    }

    public bool IsGroupSelectorEnabled => RollcallState != StateActive;
    public bool IsRollcallActive => RollcallState == StateActive;

    private int _presentCount;
    public int PresentCount
    {
        get => _presentCount;
        private set => Set(ref _presentCount, value);
    }

    private int _absentCount;
    public int AbsentCount
    {
        get => _absentCount;
        private set => Set(ref _absentCount, value);
    }

    private double _percent;
    public double Percent
    {
        get => _percent;
        private set
        {
            if (Set(ref _percent, value))
            {
                OnPropertyChanged(nameof(PercentText));
                OnPropertyChanged(nameof(RatioText));
            }
        }
    }

    public string PercentText => Rows.Count == 0 ? "0%" : $"{(int)Math.Round(Percent * 100)}%";
    public string RatioText => $"{PresentCount}/{Rows.Count}";

    private ImageSource? _sessionQr;
    public ImageSource? SessionQr
    {
        get => _sessionQr;
        private set => Set(ref _sessionQr, value);
    }

    private string _sessionFrameText = string.Empty;
    public string SessionFrameText
    {
        get => _sessionFrameText;
        private set => Set(ref _sessionFrameText, value);
    }

    private string _rollcallState = StateIdle;
    public string RollcallState
    {
        get => _rollcallState;
        private set
        {
            if (Set(ref _rollcallState, value))
            {
                OnPropertyChanged(nameof(IsGroupSelectorEnabled));
                OnPropertyChanged(nameof(IsRollcallActive));
                OnPropertyChanged(nameof(ButtonText));
                CommandManagerRefresh();
            }
        }
    }

    public string ButtonText => RollcallState switch
    {
        StateActive => "Завершить перекличку",
        StateFinished => "Завершено",
        _ => "Начать перекличку",
    };

    public string RollId => _rollId;

    private static void CommandManagerRefresh()
    {
        Application.Current?.Dispatcher.BeginInvoke(System.Windows.Input.CommandManager.InvalidateRequerySuggested);
    }

    private void StartOrStop()
    {
        if (RollcallState == StateFinished) return;
        if (RollcallState == StateActive)
        {
            var choice = ConfirmWindow.Show(
                "Завершить перекличку?",
                $"Присутствуют {PresentCount} из {Rows.Count}. Занятие запишется в журнал. Перед записью можно проверить случайных студентов.",
                "Записать",
                "Проверить случайных");
            if (choice == ConfirmChoice.Extra)
            {
                ShowSpotCheck();
                return;
            }
            if (choice != ConfirmChoice.Ok) return;
            Finish();
            return;
        }
        if (SelectedGroup is null)
        {
            _toasts.Error("Выберите группу", "Сначала выберите группу из списка.");
            return;
        }
        if (Rows.Count == 0)
        {
            _toasts.Error("В группе нет студентов", "Добавьте студентов на вкладке «Студенты».");
            return;
        }

        foreach (var row in Rows)
        {
            row.IsPresent = false;
            row.Suspicious = false;
            row.MarkedAt = null;
        }
        _lessonStartedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        _rollId = QrProtocol.NewRollId();
        var students = _database.GetStudents(SelectedGroup.Id);
        var body = QrProtocol.BuildSessionBody(students.Select(student =>
            (student.Id, student.Login, student.Password, student.FullName, student.DeviceId)));
        _sessionFrames = QrProtocol.EncodeSession(_rollId, body);
        _sessionFrameIndex = 0;
        ShowCurrentFrame();
        _cycleTimer?.Stop();
        if (_sessionFrames.Count > 1)
        {
            _cycleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _cycleTimer.Tick += (_, _) =>
            {
                _sessionFrameIndex = (_sessionFrameIndex + 1) % _sessionFrames.Count;
                ShowCurrentFrame();
            };
            _cycleTimer.Start();
        }
        RecalcStats();
        RollcallState = StateActive;
        ShowSessionWindow();
    }

    private void ShowCurrentFrame()
    {
        if (_sessionFrames.Count == 0) return;
        SessionQr = QrImages.FromText(_sessionFrames[_sessionFrameIndex]);
        SessionFrameText = _sessionFrames.Count == 1
            ? "Один код. Наведите камеру телефона преподавателя."
            : $"Кадр {_sessionFrameIndex + 1} из {_sessionFrames.Count}. Держите камеру, пока телефон не напишет «Группа считана».";
    }

    public void ShowSessionWindow()
    {
        if (RollcallState != StateActive || _sessionFrames.Count == 0) return;
        if (_sessionWindow is { IsVisible: true })
        {
            _sessionWindow.Activate();
            return;
        }
        _sessionWindow = new SessionWindow
        {
            DataContext = this,
            Owner = Application.Current?.MainWindow,
        };
        _sessionWindow.Show();
    }

    private void OpenScanner()
    {
        if (RollcallState != StateActive) return;
        var window = new ScanWindow(_rollId)
        {
            Owner = Application.Current?.MainWindow,
        };
        window.ResultReady += ApplyResultBody;
        window.Show();
    }

    private void ImportFile()
    {
        if (RollcallState != StateActive) return;
        var dialog = new OpenFileDialog
        {
            Title = "Итог с телефона преподавателя",
            Filter = "Итог переклички (*.kjournal)|*.kjournal|Текстовые файлы (*.txt)|*.txt|Все файлы|*.*",
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            ApplyTransferText(File.ReadAllText(dialog.FileName));
        }
        catch (Exception ex)
        {
            _toasts.Error("Не удалось прочитать файл", ex.Message);
        }
    }

    public void ApplyTransferText(string text)
    {
        if (RollcallState != StateActive) return;
        var collector = new QrProtocol.Collector();
        string? body = null;
        var sawForeign = false;
        foreach (var line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!QrProtocol.TryParseFrame(line, out var frame) || frame.Kind != 'R') continue;
            if (!string.Equals(frame.RollId, _rollId, StringComparison.OrdinalIgnoreCase))
            {
                sawForeign = true;
                continue;
            }
            if (collector.Add(frame, out var decoded)) body = decoded;
        }
        if (body is null)
        {
            _toasts.Error(sawForeign ? "Это итог другой переклички" : "Файл неполный",
                sawForeign
                    ? "Начните перекличку заново и отсканируйте новый код сессии."
                    : "Сохраните файл на телефоне ещё раз или покажите QR камере ПК.");
            return;
        }
        ApplyResultBody(body);
    }

    private void ApplyResultBody(string body)
    {
        if (RollcallState != StateActive) return;
        var added = 0;
        var already = 0;
        foreach (var mark in QrProtocol.ParseMarks(body))
        {
            if (mark.Code == 'S')
            {
                var attempted = _database.FindByLogin(mark.Extra);
                FlagSuspicious(mark.Id, attempted?.FullName ?? mark.Extra);
                continue;
            }
            if (mark.Code != 'P') continue;
            var row = Rows.FirstOrDefault(item => item.StudentId == mark.Id);
            if (row is null) continue;
            if (!AcceptDevice(row, mark.Extra)) continue;
            if (row.IsPresent)
            {
                already++;
                continue;
            }
            row.IsPresent = true;
            row.MarkedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            added++;
            _toasts.Success("Отмечен", row.FullName);
        }
        RecalcStats();
        if (added == 0 && already == 0)
        {
            _toasts.Info("Итог считан", "Новых отметок нет.");
        }
        else if (added > 1)
        {
            _toasts.Success("Итог считан", $"Новых отметок: {added}.");
        }
        if (Rows.Count > 0 && Rows.All(row => row.IsPresent))
        {
            Finish();
        }
    }

    private bool AcceptDevice(StudentRow row, string device)
    {
        if (device.Length == 0) return true;
        var owner = _database.FindStudentIdByDevice(device);
        if (owner is int ownerId && ownerId != row.StudentId)
        {
            FlagSuspicious(ownerId, row.FullName);
            return false;
        }
        var bound = _database.GetDeviceId(row.StudentId);
        if (bound is null)
        {
            _database.SetDeviceId(row.StudentId, device);
            row.PhoneId = device;
            return true;
        }
        if (!string.Equals(bound, device, StringComparison.Ordinal))
        {
            _toasts.Error("Аккаунт привязан к другому телефону", row.FullName);
            return false;
        }
        row.PhoneId = device;
        return true;
    }

    private void FlagSuspicious(int ownerId, string attemptedName)
    {
        var row = Rows.FirstOrDefault(item => item.StudentId == ownerId);
        if (row is not null) row.Suspicious = true;
        var ownerName = row?.FullName ?? _database.GetStudentName(ownerId) ?? "неизвестного студента";
        _toasts.Error("Что-то не так", $"Телефон студента {ownerName} использовали для отметки «{attemptedName}»");
    }

    private void TogglePresent(StudentRow row)
    {
        if (RollcallState != StateActive) return;
        if (row.IsPresent)
        {
            row.IsPresent = false;
            row.MarkedAt = null;
            RecalcStats();
            return;
        }
        row.IsPresent = true;
        row.MarkedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        _toasts.Success("Отмечен", row.FullName);
        RecalcStats();
        if (Rows.All(item => item.IsPresent)) Finish();
    }

    private void ShowSpotCheck()
    {
        var present = Rows.Where(row => row.IsPresent).ToList();
        if (present.Count == 0)
        {
            _toasts.Error("Некого проверять", "Сначала отметьте студентов камерой.");
            return;
        }
        var count = Math.Min(3, present.Count);
        var picked = present.OrderBy(_ => Random.Shared.Next()).Take(count).ToList();
        SpotCheckWindow.Show(picked, row =>
        {
            row.IsPresent = false;
            row.MarkedAt = null;
            RecalcStats();
            _toasts.Info("Снят с занятия", row.FullName);
        });
    }

    private void Finish()
    {
        if (RollcallState != StateActive) return;
        _cycleTimer?.Stop();
        _cycleTimer = null;
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        if (SelectedGroup is not null)
        {
            var lessonId = _database.AddLesson(SelectedGroup.Id, _lessonStartedAt, now);
            _database.AddAttendance(lessonId, Rows.Select(row =>
                (row.StudentId, row.IsPresent, row.MarkedAt ?? now)));
        }
        try { _sessionWindow?.Close(); } catch { /* окно уже закрыто */ }
        RollcallState = StateFinished;
        _toasts.Success("Перекличка завершена", $"Присутствуют {PresentCount} из {Rows.Count}.");
        _resetTimer?.Stop();
        _resetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _resetTimer.Tick += (_, _) =>
        {
            _resetTimer?.Stop();
            _resetTimer = null;
            RollcallState = StateIdle;
            SessionQr = null;
            SessionFrameText = string.Empty;
        };
        _resetTimer.Start();
    }

    private void RecalcStats()
    {
        var present = Rows.Count(row => row.IsPresent);
        PresentCount = present;
        AbsentCount = Rows.Count - present;
        Percent = Rows.Count == 0 ? 0 : (double)present / Rows.Count;
        OnPropertyChanged(nameof(PercentText));
        OnPropertyChanged(nameof(RatioText));
    }

    public void OnActivated()
    {
        ReloadGroups();
        if (RollcallState != StateActive) LoadStudents();
    }

    private void OnDataChanged()
    {
        var marked = Rows.Where(row => row.IsPresent).ToDictionary(row => row.StudentId, row => row.MarkedAt);
        var suspicious = Rows.Where(row => row.Suspicious).Select(row => row.StudentId).ToHashSet();
        ReloadGroups();
        if (RollcallState is StateActive or StateFinished)
        {
            LoadStudents();
            foreach (var row in Rows)
            {
                if (marked.TryGetValue(row.StudentId, out var markedAt))
                {
                    row.IsPresent = true;
                    row.MarkedAt = markedAt;
                }
                if (suspicious.Contains(row.StudentId)) row.Suspicious = true;
            }
            RecalcStats();
        }
    }

    private void ReloadGroups()
    {
        var selectedId = SelectedGroup?.Id;
        Groups.Clear();
        foreach (var group in _database.GetGroups()) Groups.Add(group);
        if (selectedId is int id) SelectedGroup = Groups.FirstOrDefault(group => group.Id == id);
        SelectedGroup ??= Groups.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedGroup));
    }

    private void LoadStudents()
    {
        Rows.Clear();
        if (SelectedGroup is null)
        {
            RecalcStats();
            return;
        }
        var number = 1;
        foreach (var student in _database.GetStudents(SelectedGroup.Id))
        {
            Rows.Add(new StudentRow
            {
                StudentId = student.Id,
                Number = number++,
                FullName = student.FullName,
                PhoneId = student.DeviceId,
            });
        }
        RecalcStats();
    }
}
