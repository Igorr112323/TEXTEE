using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Visits11.Services;

public sealed class ReminderService
{
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _timer;
    private DateTime? _fireAt;
    private bool _fired;

    public ReminderService(AppSettings settings)
    {
        _settings = settings;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _timer.Tick += (_, _) => Tick();
    }

    public event Action? Fired;

    public bool Armed => _fireAt is not null && !_fired;

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    public void Arm(int fromMinute, int toMinute)
    {
        var from = Math.Clamp(fromMinute, 1, 180);
        var to = Math.Clamp(toMinute, from, 180);
        _settings.SuddenFrom = from;
        _settings.SuddenTo = to;
        _settings.Save();
        _fired = false;
        _fireAt = DateTime.Now.AddMinutes(Random.Shared.Next(from, to + 1));
    }

    public void Disarm()
    {
        _fireAt = null;
        _fired = false;
    }

    private void Tick()
    {
        if (_fireAt is null || _fired || DateTime.Now < _fireAt) return;
        _fired = true;
        try { MessageBeep(0x30); } catch { }
        Fired?.Invoke();
    }

    [DllImport("user32.dll")]
    private static extern bool MessageBeep(uint type);
}
