using Avalonia.Styling;

namespace MMV.App.Services;

public interface IThemeService
{
    bool IsDarkMode { get; }
    void SetTheme(bool isDark);
    void ToggleTheme();
    event EventHandler? ThemeChanged;
}
