using System.Windows.Input;
using MMV.App.Commands;
using MMV.App.Time;
using MMV.Application.UseCases.Customers.CreateCustomer;
using MMV.Application.UseCases.Customers.ListCustomers;
using MMV.Application.UseCases.Customers.UpdateCustomer;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel pour le formulaire de création/édition d'un client.
/// </summary>
/// <remarks>
/// P2C-2 : la persistance directe (repository + <c>IUnitOfWork</c> + <c>SaveChangesAsync</c>) a été déplacée vers
/// la couche Application. La ViewModel ne fait plus qu'orchestrer l'écran (état, validation de surface,
/// construction des commandes) puis déléguer à <see cref="ICreateCustomerUseCase"/> /
/// <see cref="IUpdateCustomerUseCase"/>.
/// <para>P2D-7C : le pré-remplissage d'édition (<see cref="InitializeForEdit"/>) et l'événement
/// <see cref="CustomerSaved"/> portent désormais le DTO applicatif <see cref="CustomerListItemDto"/> (jamais
/// l'entité EF <c>Customer</c>).</para>
/// </remarks>
public class CustomerFormViewModel : BaseViewModel
{
    private readonly ICreateCustomerUseCase _createCustomerUseCase;
    private readonly IUpdateCustomerUseCase _updateCustomerUseCase;
    private CustomerListItemDto? _originalCustomer;

    #region Properties

    private long _customerId;
    public long CustomerId
    {
        get => _customerId;
        set => SetProperty(ref _customerId, value);
    }

    private string _firstName = string.Empty;
    public string FirstName
    {
        get => _firstName;
        set
        {
            if (SetProperty(ref _firstName, value))
            {
                ValidateProperty(nameof(FirstName));
                SaveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private string _lastName = string.Empty;
    public string LastName
    {
        get => _lastName;
        set
        {
            if (SetProperty(ref _lastName, value))
            {
                ValidateProperty(nameof(LastName));
                SaveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private string _email = string.Empty;
    public string Email
    {
        get => _email;
        set
        {
            if (SetProperty(ref _email, value))
            {
                ValidateProperty(nameof(Email));
                SaveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private string _phone = string.Empty;
    public string Phone
    {
        get => _phone;
        set
        {
            if (SetProperty(ref _phone, value))
            {
                ValidateProperty(nameof(Phone));
                SaveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    // P4-5D : le type reste DateTimeOffset? — c'est ce que le DatePicker d'Avalonia sait lier, et lui seul.
    // La frontière avec le Domain (DateOnly?) est franchie par DatePickerCivilDate, jamais par .UtcDateTime :
    // cette dernière décalait la date d'un jour dès que l'heure locale était en avance sur UTC
    // (ADR-PROD-DB-004 §2.4, obligation T6).
    private DateTimeOffset? _birthDate;
    public DateTimeOffset? BirthDate
    {
        get => _birthDate;
        set => SetProperty(ref _birthDate, value);
    }

    private string _address = string.Empty;
    public string Address
    {
        get => _address;
        set => SetProperty(ref _address, value);
    }

    private string _city = string.Empty;
    public string City
    {
        get => _city;
        set => SetProperty(ref _city, value);
    }

    private string _postalCode = string.Empty;
    public string PostalCode
    {
        get => _postalCode;
        set => SetProperty(ref _postalCode, value);
    }

    private string _socialSecurityNumber = string.Empty;
    public string SocialSecurityNumber
    {
        get => _socialSecurityNumber;
        set => SetProperty(ref _socialSecurityNumber, value);
    }

    private string _insuranceName = string.Empty;
    public string InsuranceName
    {
        get => _insuranceName;
        set => SetProperty(ref _insuranceName, value);
    }

    private string _notes = string.Empty;
    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    private bool _isEditMode;
    public bool IsEditMode
    {
        get => _isEditMode;
        private set => SetProperty(ref _isEditMode, value);
    }

    private string _firstNameError = string.Empty;
    public string FirstNameError
    {
        get => _firstNameError;
        set => SetProperty(ref _firstNameError, value);
    }

    private string _lastNameError = string.Empty;
    public string LastNameError
    {
        get => _lastNameError;
        set => SetProperty(ref _lastNameError, value);
    }

    private string _emailError = string.Empty;
    public string EmailError
    {
        get => _emailError;
        set => SetProperty(ref _emailError, value);
    }

    private string _phoneError = string.Empty;
    public string PhoneError
    {
        get => _phoneError;
        set => SetProperty(ref _phoneError, value);
    }

    private bool _isSaving;
    public bool IsSaving
    {
        get => _isSaving;
        set
        {
            if (SetProperty(ref _isSaving, value))
            {
                SaveCommand.RaiseCanExecuteChanged();
                CancelCommand.RaiseCanExecuteChanged();
            }
        }
    }

    #endregion

    #region Commands

    public RelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }
    public ICommand BackCommand => CancelCommand; // Alias pour le BackButton

    #endregion

    #region Events

    /// <summary>
    /// Événement déclenché lorsque le formulaire est enregistré avec succès.
    /// </summary>
    public event EventHandler<CustomerListItemDto>? CustomerSaved;

    /// <summary>
    /// Événement déclenché lorsque le formulaire est annulé.
    /// </summary>
    public event EventHandler? Cancelled;

    #endregion

    public CustomerFormViewModel(ICreateCustomerUseCase createCustomerUseCase, IUpdateCustomerUseCase updateCustomerUseCase)
    {
        _createCustomerUseCase = createCustomerUseCase ?? throw new ArgumentNullException(nameof(createCustomerUseCase));
        _updateCustomerUseCase = updateCustomerUseCase ?? throw new ArgumentNullException(nameof(updateCustomerUseCase));

        SaveCommand = new RelayCommand(ExecuteSave, CanExecuteSave);
        CancelCommand = new RelayCommand(ExecuteCancel, CanExecuteCancel);

        Title = "Nouveau Client";
    }

    /// <summary>
    /// Initialise le formulaire pour créer un nouveau client.
    /// </summary>
    public void InitializeForCreate()
    {
        IsEditMode = false;
        Title = "Nouveau Client";
        ClearForm();
    }

    /// <summary>
    /// Initialise le formulaire pour éditer un client existant.
    /// </summary>
    public void InitializeForEdit(CustomerListItemDto customer)
    {
        IsEditMode = true;
        Title = $"Modifier {customer.FirstName} {customer.LastName}";
        _originalCustomer = customer;

        // Copier les données du client dans le formulaire
        CustomerId = customer.CustomerId;
        FirstName = customer.FirstName;
        LastName = customer.LastName;
        Email = customer.Email ?? string.Empty;
        Phone = customer.Phone ?? string.Empty;
        BirthDate = DatePickerCivilDate.ToPickerValue(customer.BirthDate);
        Address = customer.Address ?? string.Empty;
        City = customer.City ?? string.Empty;
        PostalCode = customer.PostalCode ?? string.Empty;
        SocialSecurityNumber = customer.SocialSecurityNumber ?? string.Empty;
        InsuranceName = customer.InsuranceName ?? string.Empty;
        Notes = customer.Notes ?? string.Empty;
    }

    private void ClearForm()
    {
        CustomerId = 0;
        FirstName = string.Empty;
        LastName = string.Empty;
        Email = string.Empty;
        Phone = string.Empty;
        BirthDate = null;
        Address = string.Empty;
        City = string.Empty;
        PostalCode = string.Empty;
        SocialSecurityNumber = string.Empty;
        InsuranceName = string.Empty;
        Notes = string.Empty;

        ClearErrors();
    }

    private void ClearErrors()
    {
        FirstNameError = string.Empty;
        LastNameError = string.Empty;
        EmailError = string.Empty;
        PhoneError = string.Empty;
        ErrorMessage = string.Empty;
    }

    private void ValidateProperty(string propertyName)
    {
        switch (propertyName)
        {
            case nameof(FirstName):
                FirstNameError = string.IsNullOrWhiteSpace(FirstName) 
                    ? "Le prénom est requis" 
                    : string.Empty;
                break;

            case nameof(LastName):
                LastNameError = string.IsNullOrWhiteSpace(LastName) 
                    ? "Le nom est requis" 
                    : string.Empty;
                break;

            case nameof(Email):
                if (string.IsNullOrWhiteSpace(Email))
                {
                    EmailError = string.Empty;
                }
                else if (!Email.Contains("@") || !Email.Contains("."))
                {
                    EmailError = "Format d'email invalide";
                }
                else
                {
                    EmailError = string.Empty;
                }
                break;

            case nameof(Phone):
                if (string.IsNullOrWhiteSpace(Phone))
                {
                    PhoneError = string.Empty;
                }
                else if (Phone.Length < 10)
                {
                    PhoneError = "Le numéro de téléphone doit contenir au moins 10 chiffres";
                }
                else
                {
                    PhoneError = string.Empty;
                }
                break;
        }
    }

    private bool IsFormValid()
    {
        // Valider tous les champs
        ValidateProperty(nameof(FirstName));
        ValidateProperty(nameof(LastName));
        ValidateProperty(nameof(Email));
        ValidateProperty(nameof(Phone));

        return string.IsNullOrEmpty(FirstNameError) &&
               string.IsNullOrEmpty(LastNameError) &&
               string.IsNullOrEmpty(EmailError) &&
               string.IsNullOrEmpty(PhoneError);
    }

    private bool CanExecuteSave()
    {
        return !IsSaving && 
               !string.IsNullOrWhiteSpace(FirstName) && 
               !string.IsNullOrWhiteSpace(LastName);
    }

    private async void ExecuteSave()
    {
        if (!IsFormValid())
        {
            ErrorMessage = "Veuillez corriger les erreurs dans le formulaire";
            return;
        }

        IsSaving = true;
        ErrorMessage = string.Empty;

        try
        {
            // P2C-2 : la persistance est déléguée à la couche Application. La ViewModel se contente de construire
            // la commande à partir de l'état du formulaire, d'appeler le use case et de réagir au résultat.
            if (IsEditMode && _originalCustomer != null)
            {
                var result = await _updateCustomerUseCase.ExecuteAsync(new UpdateCustomerCommand
                {
                    CustomerId = _originalCustomer.CustomerId,
                    FirstName = FirstName,
                    LastName = LastName,
                    Email = Email,
                    Phone = Phone,
                    BirthDate = DatePickerCivilDate.ToCivilDate(BirthDate),
                    Address = Address,
                    City = City,
                    PostalCode = PostalCode,
                    SocialSecurityNumber = SocialSecurityNumber,
                    InsuranceName = InsuranceName,
                    Notes = Notes
                });

                if (!result.CustomerFound)
                {
                    ErrorMessage = "Le client à modifier est introuvable.";
                    return;
                }

                // Reconstruit un DTO à jour depuis l'état du formulaire (cohérent avec le flux d'origine qui mutait
                // l'entité chargée) avant de notifier ; la liste est ensuite rechargée par le parent.
                var updated = BuildDtoFromForm(_originalCustomer.CustomerId, _originalCustomer.CreatedAt);
                CustomerSaved?.Invoke(this, updated);
            }
            else
            {
                var result = await _createCustomerUseCase.ExecuteAsync(new CreateCustomerCommand
                {
                    FirstName = FirstName,
                    LastName = LastName,
                    Email = Email,
                    Phone = Phone,
                    BirthDate = DatePickerCivilDate.ToCivilDate(BirthDate),
                    Address = Address,
                    City = City,
                    PostalCode = PostalCode,
                    SocialSecurityNumber = SocialSecurityNumber,
                    InsuranceName = InsuranceName,
                    Notes = Notes
                });

                var created = BuildDtoFromForm(result.CustomerId, DateTime.UtcNow);
                CustomerSaved?.Invoke(this, created);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur lors de l'enregistrement : {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>
    /// Construit un <see cref="CustomerListItemDto"/> (champs optionnels normalisés en <c>null</c> si blancs) à
    /// partir de l'état du formulaire, destiné uniquement à l'affichage / à la notification
    /// <see cref="CustomerSaved"/>. La persistance reste assurée par les use cases ; cette construction ne déclenche
    /// aucune écriture.
    /// </summary>
    private CustomerListItemDto BuildDtoFromForm(long customerId, DateTime createdAt) => new()
    {
        CustomerId = customerId,
        FirstName = FirstName,
        LastName = LastName,
        Email = string.IsNullOrWhiteSpace(Email) ? null : Email,
        Phone = string.IsNullOrWhiteSpace(Phone) ? null : Phone,
        BirthDate = DatePickerCivilDate.ToCivilDate(BirthDate),
        Address = string.IsNullOrWhiteSpace(Address) ? null : Address,
        City = string.IsNullOrWhiteSpace(City) ? null : City,
        PostalCode = string.IsNullOrWhiteSpace(PostalCode) ? null : PostalCode,
        SocialSecurityNumber = string.IsNullOrWhiteSpace(SocialSecurityNumber) ? null : SocialSecurityNumber,
        InsuranceName = string.IsNullOrWhiteSpace(InsuranceName) ? null : InsuranceName,
        Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes,
        CreatedAt = createdAt,
    };

    private bool CanExecuteCancel()
    {
        return !IsSaving;
    }

    private void ExecuteCancel()
    {
        Cancelled?.Invoke(this, EventArgs.Empty);
    }
}
