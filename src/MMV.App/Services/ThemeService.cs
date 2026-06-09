using Avalonia;
using Avalonia.Styling;

namespace MMV.App.Services;

public class ThemeService : IThemeService
{
    private bool _isDarkMode;

    public bool IsDarkMode => _isDarkMode;

    public event EventHandler? ThemeChanged;

    public ThemeService()
    {
        // Light mode par défaut
        _isDarkMode = false;
    }

    public void SetTheme(bool isDark)
    {
        if (_isDarkMode == isDark) return;

        _isDarkMode = isDark;

        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = isDark
                ? ThemeVariant.Dark
                : ThemeVariant.Light;
        }

        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleTheme()
    {
        SetTheme(!_isDarkMode);
    }
}
