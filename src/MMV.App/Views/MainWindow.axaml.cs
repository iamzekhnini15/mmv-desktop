using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using MMV.App.Services;
using MMV.App.ViewModels;

namespace MMV.App.Views;

/// <summary>
/// Code-behind de la fenêtre principale.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// Constructeur sans paramètres requis par XAML.
    /// </summary>
    [Obsolete("Ce constructeur est requis par XAML. Utilisez plutôt le constructeur avec injection de dépendances.")]
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Constructeur avec injection de dépendances.
    /// </summary>
    public MainWindow(INavigationService navigationService)
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel(navigationService);
    }
}
