using System.Windows.Input;
using MMV.App.Commands;
using MMV.App.Services;
using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel coordinateur pour le module Gestion Utilisateurs.
/// Gère la navigation entre la liste et le formulaire utilisateur.
/// Accessible uniquement aux ADMIN.
/// </summary>
public class UsersViewModel : BaseViewModel
{
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISessionService _sessionService;
    private readonly IPermissionService _permissionService;
    private readonly IDialogService _dialogService;
    private readonly Domain.Services.IAuthenticationService _authenticationService;

    private UsersListViewModel? _listViewModel;
    private UserFormViewModel? _formViewModel;
    private bool _isInEditMode;

    public UsersListViewModel? ListViewModel
    {
        get => _listViewModel;
        set => SetProperty(ref _listViewModel, value);
    }

    public UserFormViewModel? FormViewModel
    {
        get => _formViewModel;
        set => SetProperty(ref _formViewModel, value);
    }

    public bool IsInEditMode
    {
        get => _isInEditMode;
        set
        {
            if (SetProperty(ref _isInEditMode, value))
                OnPropertyChanged(nameof(ShowList));
        }
    }

    public bool ShowList => !IsInEditMode;

    public UsersViewModel(
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        ISessionService sessionService,
        IPermissionService permissionService,
        IDialogService dialogService,
        Domain.Services.IAuthenticationService authenticationService)
    {
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _authenticationService = authenticationService ?? throw new ArgumentNullException(nameof(authenticationService));

        Title = "Gestion des Utilisateurs";

        InitializeListViewModel();
    }

    private void InitializeListViewModel()
    {
        ListViewModel = new UsersListViewModel(_userRepository, _unitOfWork, _dialogService);
        ListViewModel.CreateUserRequested += OnCreateUserRequested;
        ListViewModel.EditUserRequested += OnEditUserRequested;
    }

    private void OnCreateUserRequested(object? sender, EventArgs e)
    {
        FormViewModel = new UserFormViewModel(_userRepository, _unitOfWork, _authenticationService);
        FormViewModel.InitializeForCreate();
        FormViewModel.UserSaved += OnUserSaved;
        FormViewModel.Cancelled += OnFormCancelled;
        IsInEditMode = true;
    }

    private void OnEditUserRequested(object? sender, User user)
    {
        FormViewModel = new UserFormViewModel(_userRepository, _unitOfWork, _authenticationService);
        FormViewModel.InitializeForEdit(user);
        FormViewModel.UserSaved += OnUserSaved;
        FormViewModel.Cancelled += OnFormCancelled;
        IsInEditMode = true;
    }

    private async void OnUserSaved(object? sender, EventArgs e)
    {
        IsInEditMode = false;
        FormViewModel = null;
        await ListViewModel!.LoadUsersAsync();
    }

    private void OnFormCancelled(object? sender, EventArgs e)
    {
        IsInEditMode = false;
        FormViewModel = null;
    }
}
