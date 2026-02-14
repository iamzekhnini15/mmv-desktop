using System.Windows.Input;
using MMV.App.Commands;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Interfaces.Repositories;
using MMV.Domain.Services;
using MMV.Domain.Validators;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel pour le formulaire de création/édition d'un utilisateur.
/// Inclut validation des champs et hachage BCrypt du mot de passe.
/// </summary>
public class UserFormViewModel : BaseViewModel
{
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuthenticationService _authenticationService;
    
    private bool _isEditMode;
    private long _userId;
    private string _username = string.Empty;
    private string _firstName = string.Empty;
    private string _lastName = string.Empty;
    private UserRole _selectedRole = UserRole.Optician;
    private string _password = string.Empty;
    private string _confirmPassword = string.Empty;
    private bool _isActive = true;
    private bool _showPassword;
    private string _generatedPassword = string.Empty;

    // Erreurs de validation
    private string _usernameError = string.Empty;
    private string _firstNameError = string.Empty;
    private string _lastNameError = string.Empty;
    private string _passwordError = string.Empty;
    private string _confirmPasswordError = string.Empty;

    private int _passwordStrength;

    #region Properties

    public bool IsEditMode
    {
        get => _isEditMode;
        set
        {
            if (SetProperty(ref _isEditMode, value))
            {
                OnPropertyChanged(nameof(FormTitle));
                OnPropertyChanged(nameof(IsPasswordRequired));
            }
        }
    }

    public string FormTitle => IsEditMode ? "Modifier l'utilisateur" : "Nouvel utilisateur";
    public bool IsPasswordRequired => !IsEditMode;

    public string Username
    {
        get => _username;
        set
        {
            if (SetProperty(ref _username, value))
            {
                ValidateUsername();
                ((RelayCommand)SaveCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public string FirstName
    {
        get => _firstName;
        set
        {
            if (SetProperty(ref _firstName, value))
            {
                ValidateFirstName();
                ((RelayCommand)SaveCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public string LastName
    {
        get => _lastName;
        set
        {
            if (SetProperty(ref _lastName, value))
            {
                ValidateLastName();
                ((RelayCommand)SaveCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public UserRole SelectedRole
    {
        get => _selectedRole;
        set => SetProperty(ref _selectedRole, value);
    }

    public string Password
    {
        get => _password;
        set
        {
            if (SetProperty(ref _password, value))
            {
                ValidatePassword();
                ValidateConfirmPassword();
                PasswordStrength = UserValidator.GetPasswordStrength(value);
                ((RelayCommand)SaveCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public string ConfirmPassword
    {
        get => _confirmPassword;
        set
        {
            if (SetProperty(ref _confirmPassword, value))
            {
                ValidateConfirmPassword();
                ((RelayCommand)SaveCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public bool ShowPassword
    {
        get => _showPassword;
        set => SetProperty(ref _showPassword, value);
    }

    public string GeneratedPassword
    {
        get => _generatedPassword;
        set => SetProperty(ref _generatedPassword, value);
    }

    public int PasswordStrength
    {
        get => _passwordStrength;
        set
        {
            if (SetProperty(ref _passwordStrength, value))
            {
                OnPropertyChanged(nameof(PasswordStrengthText));
                OnPropertyChanged(nameof(PasswordStrengthColor));
            }
        }
    }

    public string PasswordStrengthText => PasswordStrength switch
    {
        0 => "",
        1 => "Faible",
        2 => "Moyen",
        3 => "Fort",
        4 => "Très fort",
        _ => ""
    };

    public string PasswordStrengthColor => PasswordStrength switch
    {
        0 => "#55555A",
        1 => "#FF453A",
        2 => "#FFD60A",
        3 => "#30D158",
        4 => "#30D158",
        _ => "#55555A"
    };

    // Erreurs
    public string UsernameError
    {
        get => _usernameError;
        set => SetProperty(ref _usernameError, value);
    }

    public string FirstNameError
    {
        get => _firstNameError;
        set => SetProperty(ref _firstNameError, value);
    }

    public string LastNameError
    {
        get => _lastNameError;
        set => SetProperty(ref _lastNameError, value);
    }

    public string PasswordError
    {
        get => _passwordError;
        set => SetProperty(ref _passwordError, value);
    }

    public string ConfirmPasswordError
    {
        get => _confirmPasswordError;
        set => SetProperty(ref _confirmPasswordError, value);
    }

    public UserRole[] AvailableRoles { get; } = { UserRole.Admin, UserRole.Optician, UserRole.Technician };

    #endregion

    #region Commands

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand GeneratePasswordCommand { get; }
    public ICommand ToggleShowPasswordCommand { get; }

    #endregion

    #region Events

    public event EventHandler? UserSaved;
    public event EventHandler? Cancelled;

    #endregion

    public UserFormViewModel(
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        IAuthenticationService authenticationService)
    {
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _authenticationService = authenticationService ?? throw new ArgumentNullException(nameof(authenticationService));

        SaveCommand = new RelayCommand(async () => await ExecuteSaveAsync(), CanSave);
        CancelCommand = new RelayCommand(() => Cancelled?.Invoke(this, EventArgs.Empty));
        GeneratePasswordCommand = new RelayCommand(ExecuteGeneratePassword);
        ToggleShowPasswordCommand = new RelayCommand(() => ShowPassword = !ShowPassword);
    }

    public void InitializeForCreate()
    {
        IsEditMode = false;
        _userId = 0;
        Username = string.Empty;
        FirstName = string.Empty;
        LastName = string.Empty;
        SelectedRole = UserRole.Optician;
        Password = string.Empty;
        ConfirmPassword = string.Empty;
        IsActive = true;
        GeneratedPassword = string.Empty;
        ClearErrors();
    }

    public void InitializeForEdit(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        IsEditMode = true;
        _userId = user.UserId;
        Username = user.Username;
        FirstName = user.FirstName;
        LastName = user.LastName;
        SelectedRole = user.Role;
        Password = string.Empty;
        ConfirmPassword = string.Empty;
        IsActive = user.IsActive;
        GeneratedPassword = string.Empty;
        ClearErrors();
    }

    private void ExecuteGeneratePassword()
    {
        var generated = UserValidator.GenerateStrongPassword();
        Password = generated;
        ConfirmPassword = generated;
        GeneratedPassword = generated;
        ShowPassword = true;
    }

    private async Task ExecuteSaveAsync()
    {
        if (!ValidateAll())
            return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            if (IsEditMode)
            {
                var user = await _userRepository.GetByIdAsync(_userId);
                if (user == null)
                {
                    ErrorMessage = "Utilisateur introuvable.";
                    return;
                }

                // Vérifier unicité du username si changé
                if (user.Username != Username.Trim())
                {
                    var existing = await _userRepository.GetByUsernameAsync(Username.Trim());
                    if (existing != null)
                    {
                        UsernameError = "Ce nom d'utilisateur est déjà utilisé.";
                        return;
                    }
                }

                user.Username = Username.Trim();
                user.FirstName = FirstName.Trim();
                user.LastName = LastName.Trim();
                user.Role = SelectedRole;
                user.IsActive = IsActive;

                // Si un nouveau mot de passe est fourni, le hasher
                if (!string.IsNullOrWhiteSpace(Password))
                {
                    user.PasswordHash = _authenticationService.HashPassword(Password);
                }

                await _userRepository.UpdateAsync(user);
            }
            else
            {
                // Vérifier unicité du username
                var existing = await _userRepository.GetByUsernameAsync(Username.Trim());
                if (existing != null)
                {
                    UsernameError = "Ce nom d'utilisateur est déjà utilisé.";
                    return;
                }

                var newUser = new User
                {
                    Username = Username.Trim(),
                    FirstName = FirstName.Trim(),
                    LastName = LastName.Trim(),
                    Role = SelectedRole,
                    IsActive = IsActive,
                    PasswordHash = _authenticationService.HashPassword(Password),
                    CreatedAt = DateTime.UtcNow
                };

                await _userRepository.CreateAsync(newUser);
            }

            await _unitOfWork.SaveChangesAsync();
            UserSaved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur lors de la sauvegarde : {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[UserFormVM] Erreur save : {ex}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    #region Validation

    private bool CanSave()
    {
        return !IsLoading;
    }

    private bool ValidateAll()
    {
        ClearErrors();
        bool valid = true;

        if (!ValidateUsername()) valid = false;
        if (!ValidateFirstName()) valid = false;
        if (!ValidateLastName()) valid = false;
        if (!ValidatePassword()) valid = false;
        if (!ValidateConfirmPassword()) valid = false;

        return valid;
    }

    private bool ValidateUsername()
    {
        UsernameError = string.Empty;

        if (string.IsNullOrWhiteSpace(Username))
        {
            UsernameError = "Le nom d'utilisateur est requis.";
            return false;
        }
        if (Username.Trim().Length < 3)
        {
            UsernameError = "Minimum 3 caractères.";
            return false;
        }
        if (Username.Contains(' '))
        {
            UsernameError = "Pas d'espaces autorisés.";
            return false;
        }
        if (!System.Text.RegularExpressions.Regex.IsMatch(Username, @"^[a-zA-Z0-9._-]+$"))
        {
            UsernameError = "Caractères autorisés : lettres, chiffres, points, tirets, underscores.";
            return false;
        }

        return true;
    }

    private bool ValidateFirstName()
    {
        FirstNameError = string.Empty;
        if (string.IsNullOrWhiteSpace(FirstName))
        {
            FirstNameError = "Le prénom est requis.";
            return false;
        }
        return true;
    }

    private bool ValidateLastName()
    {
        LastNameError = string.Empty;
        if (string.IsNullOrWhiteSpace(LastName))
        {
            LastNameError = "Le nom est requis.";
            return false;
        }
        return true;
    }

    private bool ValidatePassword()
    {
        PasswordError = string.Empty;

        if (!IsEditMode || !string.IsNullOrWhiteSpace(Password))
        {
            if (string.IsNullOrWhiteSpace(Password) && !IsEditMode)
            {
                PasswordError = "Le mot de passe est requis.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(Password))
            {
                var (isValid, errorMessage) = UserValidator.ValidatePasswordPolicy(Password);
                if (!isValid)
                {
                    PasswordError = errorMessage;
                    return false;
                }
            }
        }

        return true;
    }

    private bool ValidateConfirmPassword()
    {
        ConfirmPasswordError = string.Empty;

        if (!string.IsNullOrWhiteSpace(Password))
        {
            if (Password != ConfirmPassword)
            {
                ConfirmPasswordError = "Les mots de passe ne correspondent pas.";
                return false;
            }
        }

        return true;
    }

    private void ClearErrors()
    {
        UsernameError = string.Empty;
        FirstNameError = string.Empty;
        LastNameError = string.Empty;
        PasswordError = string.Empty;
        ConfirmPasswordError = string.Empty;
        ErrorMessage = null;
    }

    #endregion
}
