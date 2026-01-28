using System.Windows.Input;
using MMV.App.Commands;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel pour l'écran de connexion.
/// </summary>
public class LoginViewModel : BaseViewModel
{
    private string _username = string.Empty;
    private string _password = string.Empty;
    private string _loginError = string.Empty;
    private bool _isLoggingIn;

    /// <summary>
    /// Nom d'utilisateur saisi.
    /// </summary>
    public string Username
    {
        get => _username;
        set
        {
            if (SetProperty(ref _username, value))
            {
                ((RelayCommand)LoginCommand).RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Mot de passe saisi.
    /// </summary>
    public string Password
    {
        get => _password;
        set
        {
            if (SetProperty(ref _password, value))
            {
                ((RelayCommand)LoginCommand).RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Message d'erreur de connexion.
    /// </summary>
    public string LoginError
    {
        get => _loginError;
        set => SetProperty(ref _loginError, value);
    }

    /// <summary>
    /// Indique si une connexion est en cours.
    /// </summary>
    public bool IsLoggingIn
    {
        get => _isLoggingIn;
        set => SetProperty(ref _isLoggingIn, value);
    }

    /// <summary>
    /// Commande pour se connecter.
    /// </summary>
    public ICommand LoginCommand { get; }

    /// <summary>
    /// Événement déclenché quand la connexion réussit.
    /// </summary>
    public event EventHandler? LoginSuccessful;

    public LoginViewModel()
    {
        Title = "Connexion - ManageMyVision";
        LoginCommand = new RelayCommand(ExecuteLogin, CanLogin);
    }

    /// <summary>
    /// Exécute la connexion.
    /// </summary>
    private async void ExecuteLogin()
    {
        IsLoggingIn = true;
        ((RelayCommand)LoginCommand).RaiseCanExecuteChanged();
        LoginError = string.Empty;

        try
        {
            // Simulation d'authentification (à remplacer par le vrai service)
            await Task.Delay(500); // Simule un appel réseau

            // Validation basique pour demo
            if (Username == "admin" && Password == "admin")
            {
                // Connexion réussie
                LoginSuccessful?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                LoginError = "Nom d'utilisateur ou mot de passe incorrect.";
            }
        }
        catch (Exception ex)
        {
            LoginError = $"Erreur de connexion : {ex.Message}";
        }
        finally
        {
            IsLoggingIn = false;
            ((RelayCommand)LoginCommand).RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// Vérifie si la connexion est possible.
    /// </summary>
    private bool CanLogin()
    {
        return !string.IsNullOrWhiteSpace(Username) 
            && !string.IsNullOrWhiteSpace(Password)
            && !IsLoggingIn;
    }
}
