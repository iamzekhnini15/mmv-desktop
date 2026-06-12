using System.Windows.Input;
using MMV.App.Commands;
using MMV.App.Services;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Services;
using MMV.Domain.Validators;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel pour le profil utilisateur et le changement de mot de passe.
/// Affiche les informations de l'utilisateur connecté et permet de changer le mot de passe.
/// </summary>
public class UserProfileViewModel : BaseViewModel
{
    private readonly ISessionService _sessionService;
    private readonly IAuthenticationService _authenticationService;
    private readonly IThemeService _themeService;

    private string _currentPassword = string.Empty;
    private string _newPassword = string.Empty;
    private string _confirmNewPassword = string.Empty;
    private string _currentPasswordError = string.Empty;
    private string _newPasswordError = string.Empty;
    private string _confirmNewPasswordError = string.Empty;
    private string _successMessage = string.Empty;
    private int _newPasswordStrength;
    private bool _isDarkMode;

    #region Read-only properties

    public string Username => _sessionService.CurrentUser?.Username ?? "-";
    public string FullName => _sessionService.CurrentUser != null
        ? $"{_sessionService.CurrentUser.FirstName} {_sessionService.CurrentUser.LastName}"
        : "-";
    public string RoleDisplay => _sessionService.CurrentUser?.Role switch
    {
        UserRole.Admin => "Administrateur",
        UserRole.Optician => "Opticien",
        UserRole.Technician => "Technicien",
        _ => "-"
    };
    public string Initials => _sessionService.CurrentUser != null
        ? $"{(_sessionService.CurrentUser.FirstName.Length > 0 ? _sessionService.CurrentUser.FirstName[0] : ' ')}{(_sessionService.CurrentUser.LastName.Length > 0 ? _sessionService.CurrentUser.LastName[0] : ' ')}".ToUpper()
        : "??";
    public string LastLoginDisplay => _sessionService.CurrentUser?.LastLogin?.ToString("dd/MM/yyyy HH:mm") ?? "Jamais";
    public string CreatedAtDisplay => _sessionService.CurrentUser?.CreatedAt.ToString("dd/MM/yyyy") ?? "-";

    #endregion

    #region Password change properties

    public string CurrentPassword
    {
        get => _currentPassword;
        set
        {
            if (SetProperty(ref _currentPassword, value))
            {
                CurrentPasswordError = string.Empty;
                ((RelayCommand)ChangePasswordCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public string NewPassword
    {
        get => _newPassword;
        set
        {
            if (SetProperty(ref _newPassword, value))
            {
                NewPasswordError = string.Empty;
                ValidateNewPassword();
                ValidateConfirmNewPassword();
                NewPasswordStrength = UserValidator.GetPasswordStrength(value);
                ((RelayCommand)ChangePasswordCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public string ConfirmNewPassword
    {
        get => _confirmNewPassword;
        set
        {
            if (SetProperty(ref _confirmNewPassword, value))
            {
                ConfirmNewPasswordError = string.Empty;
                ValidateConfirmNewPassword();
                ((RelayCommand)ChangePasswordCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public string CurrentPasswordError
    {
        get => _currentPasswordError;
        set => SetProperty(ref _currentPasswordError, value);
    }

    public string NewPasswordError
    {
        get => _newPasswordError;
        set => SetProperty(ref _newPasswordError, value);
    }

    public string ConfirmNewPasswordError
    {
        get => _confirmNewPasswordError;
        set => SetProperty(ref _confirmNewPasswordError, value);
    }

    public string SuccessMessage
    {
        get => _successMessage;
        set => SetProperty(ref _successMessage, value);
    }

    public int NewPasswordStrength
    {
        get => _newPasswordStrength;
        set
        {
            if (SetProperty(ref _newPasswordStrength, value))
            {
                OnPropertyChanged(nameof(PasswordStrengthText));
                OnPropertyChanged(nameof(PasswordStrengthColor));
            }
        }
    }

    public string PasswordStrengthText => NewPasswordStrength switch
    {
        0 => "",
        1 => "Faible",
        2 => "Moyen",
        3 => "Fort",
        4 => "Très fort",
        _ => ""
    };

    public string PasswordStrengthColor => NewPasswordStrength switch
    {
        0 => "#55555A",
        1 => "#FF453A",
        2 => "#FFD60A",
        3 => "#30D158",
        4 => "#30D158",
        _ => "#55555A"
    };

    #endregion

    #region Theme properties

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

    #endregion

    #region Commands

    public ICommand ChangePasswordCommand { get; }
    public ICommand CancelCommand { get; }

    #endregion

    public event EventHandler? PasswordChanged;
    public event EventHandler? Cancelled;

    public UserProfileViewModel(
        ISessionService sessionService,
        IAuthenticationService authenticationService,
        IThemeService themeService)
    {
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        _authenticationService = authenticationService ?? throw new ArgumentNullException(nameof(authenticationService));
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));

        Title = "Mon Profil";
        _isDarkMode = _themeService.IsDarkMode;
        ChangePasswordCommand = new RelayCommand(async () => await ExecuteChangePasswordAsync(), CanChangePassword);
        CancelCommand = new RelayCommand(() => Cancelled?.Invoke(this, EventArgs.Empty));
    }

    private bool CanChangePassword()
    {
        return !string.IsNullOrWhiteSpace(CurrentPassword)
            && !string.IsNullOrWhiteSpace(NewPassword)
            && !string.IsNullOrWhiteSpace(ConfirmNewPassword)
            && !IsLoading;
    }

    private async Task ExecuteChangePasswordAsync()
    {
        if (!ValidatePasswordChange())
            return;

        IsLoading = true;
        ErrorMessage = null;
        SuccessMessage = string.Empty;

        try
        {
            var userId = _sessionService.CurrentUser?.UserId;
            if (userId == null || userId <= 0)
            {
                ErrorMessage = "Session invalide. Veuillez vous reconnecter.";
                return;
            }

            var success = await _authenticationService.ChangePasswordAsync(
                userId.Value, CurrentPassword, NewPassword);

            if (success)
            {
                SuccessMessage = "Mot de passe modifié avec succès !";
                CurrentPassword = string.Empty;
                NewPassword = string.Empty;
                ConfirmNewPassword = string.Empty;
                PasswordChanged?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                CurrentPasswordError = "Le mot de passe actuel est incorrect.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur lors du changement de mot de passe : {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[UserProfileVM] Erreur : {ex}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool ValidatePasswordChange()
    {
        bool valid = true;
        CurrentPasswordError = string.Empty;
        NewPasswordError = string.Empty;
        ConfirmNewPasswordError = string.Empty;

        if (string.IsNullOrWhiteSpace(CurrentPassword))
        {
            CurrentPasswordError = "Le mot de passe actuel est requis.";
            valid = false;
        }

        if (!ValidateNewPassword()) valid = false;
        if (!ValidateConfirmNewPassword()) valid = false;

        return valid;
    }

    private bool ValidateNewPassword()
    {
        if (!string.IsNullOrWhiteSpace(NewPassword))
        {
            var (isValid, errorMessage) = UserValidator.ValidatePasswordPolicy(NewPassword);
            if (!isValid)
            {
                NewPasswordError = errorMessage;
                return false;
            }
        }
        else if (string.IsNullOrWhiteSpace(NewPassword))
        {
            NewPasswordError = "Le nouveau mot de passe est requis.";
            return false;
        }
        return true;
    }

    private bool ValidateConfirmNewPassword()
    {
        if (!string.IsNullOrWhiteSpace(NewPassword) && NewPassword != ConfirmNewPassword)
        {
            ConfirmNewPasswordError = "Les mots de passe ne correspondent pas.";
            return false;
        }
        return true;
    }
}
