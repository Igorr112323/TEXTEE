using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Visits11.Services;

public sealed class AppSettings
{
    private readonly string _path;

    public string Theme { get; set; } = ThemeService.Light;
    public string TeacherName { get; set; } = string.Empty;
    public string WifiSsid { get; set; } = string.Empty;
    public string WifiPassword { get; set; } = string.Empty;
    public string PinHash { get; set; } = string.Empty;
    public int Port { get; set; } = 8090;
    public int SuddenFrom { get; set; } = 15;
    public int SuddenTo { get; set; } = 60;

    public AppSettings(string path) => _path = path;

    public bool IsDefaultPin => PinHash == Hash("2468");

    public bool CheckPin(string pin) => Hash(pin.Trim()) == PinHash;

    public void SetPin(string pin) => PinHash = Hash(pin.Trim());

    public static string Hash(string pin)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(pin));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public void Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(_path));
                if (dto is not null)
                {
                    if (dto.Theme is ThemeService.Light or ThemeService.Dark) Theme = dto.Theme;
                    TeacherName = dto.TeacherName ?? string.Empty;
                    WifiSsid = dto.WifiSsid ?? string.Empty;
                    WifiPassword = dto.WifiPassword ?? string.Empty;
                    PinHash = dto.PinHash ?? string.Empty;
                    if (dto.Port is >= 1024 and <= 65535) Port = dto.Port;
                    if (dto.SuddenFrom is >= 1 and <= 180) SuddenFrom = dto.SuddenFrom;
                    if (dto.SuddenTo is >= 1 and <= 180) SuddenTo = dto.SuddenTo;
                }
            }
        }
        catch
        {
        }

        if (string.IsNullOrEmpty(PinHash))
        {
            SetPin("2468");
            Save();
        }
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(new Dto
            {
                Theme = Theme,
                TeacherName = TeacherName,
                WifiSsid = WifiSsid,
                WifiPassword = WifiPassword,
                PinHash = PinHash,
                Port = Port,
                SuddenFrom = SuddenFrom,
                SuddenTo = SuddenTo,
            }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_path, json);
        }
        catch
        {
        }
    }

    private sealed class Dto
    {
        public string Theme { get; set; } = ThemeService.Light;
        public string? TeacherName { get; set; }
        public string? WifiSsid { get; set; }
        public string? WifiPassword { get; set; }
        public string? PinHash { get; set; }
        public int Port { get; set; } = 8090;
        public int SuddenFrom { get; set; } = 15;
        public int SuddenTo { get; set; } = 60;
    }
}
