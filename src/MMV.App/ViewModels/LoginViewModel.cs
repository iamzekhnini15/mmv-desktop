using System.Windows.Input;
using MMV.App.Commands;
using MMV.App.Services;
using MMV.Domain.Services;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel pour l'écran de connexion.
/// Utilise AuthenticationService (BCrypt) pour valider les identifiants.
/// </summary>
public class LoginViewModel : BaseViewModel
{
    private readonly IAuthenticationService _authenticationService;
    private readonly ISessionService _sessionService;
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

    public LoginViewModel(IAuthenticationService authenticationService, ISessionService sessionService)
    {
        _authenticationService = authenticationService ?? throw new ArgumentNullException(nameof(authenticationService));
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        Title = "Connexion - ManageMyVision";
        LoginCommand = new RelayCommand(ExecuteLogin, CanLogin);
    }

    /// <summary>
    /// Exécute la connexion avec authentification BCrypt réelle.
    /// </summary>
    private async void ExecuteLogin()
    {
        IsLoggingIn = true;
        ((RelayCommand)LoginCommand).RaiseCanExecuteChanged();
        LoginError = string.Empty;

        try
        {
            var user = await _authenticationService.AuthenticateAsync(Username.Trim(), Password);

            if (user != null)
            {
                // Stocker l'utilisateur dans la session
                _sessionService.Login(user);
                LoginSuccessful?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                // Vérifier si c'est un compte désactivé
                LoginError = "Nom d'utilisateur ou mot de passe incorrect, ou compte désactivé.";
            }
        }
        catch (Exception ex)
        {
            LoginError = $"Erreur de connexion : {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[Login] Erreur : {ex}");
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
