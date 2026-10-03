using Microsoft.Data.Sqlite;

namespace Visits11.Services;

public sealed class BackupService
{
    private readonly string _dir;
    private readonly string _dbPath;

    public string LastText { get; private set; } = "Копий ещё нет";

    public BackupService(string appDir, string dbPath)
    {
        _dir = Path.Combine(appDir, "backups");
        _dbPath = dbPath;
    }

    public void EnsureDaily()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, $"daily-{DateTime.Now:yyyyMMdd}.db");
        if (!File.Exists(path) && File.Exists(_dbPath)) Copy(path);
        Prune();
        Remember();
    }

    public void Snapshot()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, $"attendance-{DateTime.Now:yyyyMMdd-HHmmss}.db");
        Copy(path);
        Prune();
        LastText = Path.GetFileName(path);
    }

    private void Copy(string path)
    {
        using var source = new SqliteConnection($"Data Source={_dbPath}");
        source.Open();
        using var destination = new SqliteConnection($"Data Source={path}");
        destination.Open();
        source.BackupDatabase(destination);
    }

    private void Remember()
    {
        if (!Directory.Exists(_dir)) return;
        var latest = Directory.GetFiles(_dir, "*.db")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault();
        if (latest is not null) LastText = latest.Name;
    }

    private void Prune()
    {
        Drop("attendance-", 20);
        Drop("daily-", 60);
    }

    private void Drop(string prefix, int keep)
    {
        if (!Directory.Exists(_dir)) return;
        var stale = Directory.GetFiles(_dir, prefix + "*.db")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Skip(keep);
        foreach (var file in stale)
        {
            try { file.Delete(); } catch { }
        }
    }
}
