namespace MMV.App.ViewModels;

/// <summary>ViewModel pour la gestion des ordonnances.</summary>
public class PrescriptionsViewModel : BaseViewModel
{
    public PrescriptionsViewModel()
    {
        Title = "Gestion des Ordonnances";
    }
}

/// <summary>ViewModel pour la gestion des ventes (POS).</summary>
public class SalesViewModel : BaseViewModel
{
    public SalesViewModel()
    {
        Title = "Point de Vente";
    }
}

/// <summary>ViewModel pour les rapports et statistiques.</summary>
public class ReportsViewModel : BaseViewModel
{
    public ReportsViewModel()
    {
        Title = "Rapports & Statistiques";
    }
}

/// <summary>ViewModel pour les paramètres de l'application.</summary>
public class SettingsViewModel : BaseViewModel
{
    private readonly Services.IThemeService _themeService;
    private bool _isDarkMode;

    public bool IsDarkMode
    {
        get => _isDarkMode;
        set
        {
            if (SetProperty(ref _isDarkMode, value))
            {
                _themeService.SetTheme(value);
                OnPropertyChanged(nameof(ThemeLabel));
            }
        }
    }

    public string ThemeLabel => IsDarkMode ? "Mode sombre activé" : "Mode clair activé";

    public SettingsViewModel(Services.IThemeService themeService)
    {
        _themeService = themeService;
        Title = "Paramètres";
        _isDarkMode = _themeService.IsDarkMode;
    }
}
