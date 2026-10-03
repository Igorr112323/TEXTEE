using System.Windows;

namespace Visits11.Services;

public sealed class ThemeService
{
    public const string Light = "light";
    public const string Dark = "dark";

    private readonly AppSettings _settings;

    public string Current { get; private set; } = Light;
    public bool IsDark => Current == Dark;

    public event Action? ThemeChanged;

    public ThemeService(AppSettings settings)
    {
        _settings = settings;
    }

    public void LoadAndApply()
    {
        var theme = _settings.Theme is Light or Dark ? _settings.Theme : Light;
        Apply(theme, save: false);
    }

    public void Toggle() => Apply(IsDark ? Light : Dark, save: true);

    private void Apply(string theme, bool save)
    {
        Current = theme;
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var index = -1;
        for (var i = 0; i < dictionaries.Count; i++)
        {
            if (dictionaries[i].Source is { } source && source.OriginalString.Contains("Theme.", StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }
        if (index >= 0)
        {
            var file = IsDark ? "Theme.Dark" : "Theme.Light";
            dictionaries[index] = new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/Styles/{file}.xaml"),
            };
        }
        if (save)
        {
            _settings.Theme = theme;
            _settings.Save();
        }
        ThemeChanged?.Invoke();
    }
}
