using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MMV.App.ViewModels;
using MMV.App.Views;

namespace MMV.App;

/// <summary>
/// Classe principale de l'application Avalonia.
/// </summary>
public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Créer la fenêtre de connexion
            var loginViewModel = new LoginViewModel();
            var loginWindow = new Window
            {
                Title = "Connexion - ManageMyVision",
                Width = 1000,
                Height = 700,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Content = new LoginView { DataContext = loginViewModel }
            };

            // Quand la connexion réussit, afficher la fenêtre principale
            loginViewModel.LoginSuccessful += (s, e) =>
            {
                var mainWindow = new MainWindow
                {
                    DataContext = new MainWindowViewModel()
                };
                
                desktop.MainWindow = mainWindow;
                mainWindow.Show();
                loginWindow.Close();
            };

            desktop.MainWindow = loginWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
