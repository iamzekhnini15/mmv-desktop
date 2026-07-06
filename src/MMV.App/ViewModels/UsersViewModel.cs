using System.Windows.Input;
using MMV.App.Commands;
using MMV.App.Services;
using MMV.Application.UseCases.Users.CreateUser;
using MMV.Application.UseCases.Users.ListUsers;
using MMV.Application.UseCases.Users.SetUserActive;
using MMV.Application.UseCases.Users.UpdateUser;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel coordinateur pour le module Gestion Utilisateurs.
/// Gère la navigation entre la liste et le formulaire utilisateur.
/// Accessible uniquement aux ADMIN.
/// <para>
/// P2C-GLOBAL : les écritures sont déléguées aux use cases Application (create / update / activation).
/// P2D-1 : la lecture de la liste passe par <see cref="IListUsersUseCase"/> ; plus aucune dépendance
/// <c>IUserRepository</c> (le hachage et la persistance vivent dans les use cases).
/// </para>
/// </summary>
public class UsersViewModel : BaseViewModel
{
    private readonly IListUsersUseCase _listUsersUseCase;
    private readonly ICreateUserUseCase _createUserUseCase;
    private readonly IUpdateUserUseCase _updateUserUseCase;
    private readonly ISetUserActiveUseCase _setUserActiveUseCase;
    private readonly ISessionService _sessionService;
    private readonly IPermissionService _permissionService;
    private readonly IDialogService _dialogService;

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
        IListUsersUseCase listUsersUseCase,
        ICreateUserUseCase createUserUseCase,
        IUpdateUserUseCase updateUserUseCase,
        ISetUserActiveUseCase setUserActiveUseCase,
        ISessionService sessionService,
        IPermissionService permissionService,
        IDialogService dialogService)
    {
        _listUsersUseCase = listUsersUseCase ?? throw new ArgumentNullException(nameof(listUsersUseCase));
        _createUserUseCase = createUserUseCase ?? throw new ArgumentNullException(nameof(createUserUseCase));
        _updateUserUseCase = updateUserUseCase ?? throw new ArgumentNullException(nameof(updateUserUseCase));
        _setUserActiveUseCase = setUserActiveUseCase ?? throw new ArgumentNullException(nameof(setUserActiveUseCase));
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

        Title = "Gestion des Utilisateurs";

        InitializeListViewModel();
    }

    private void InitializeListViewModel()
    {
        ListViewModel = new UsersListViewModel(_listUsersUseCase, _setUserActiveUseCase, _dialogService);
        ListViewModel.CreateUserRequested += OnCreateUserRequested;
        ListViewModel.EditUserRequested += OnEditUserRequested;
    }

    private void OnCreateUserRequested(object? sender, EventArgs e)
    {
        FormViewModel = new UserFormViewModel(_createUserUseCase, _updateUserUseCase);
        FormViewModel.InitializeForCreate();
        FormViewModel.UserSaved += OnUserSaved;
        FormViewModel.Cancelled += OnFormCancelled;
        IsInEditMode = true;
    }

    private void OnEditUserRequested(object? sender, UserListItemDto user)
    {
        FormViewModel = new UserFormViewModel(_createUserUseCase, _updateUserUseCase);
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
