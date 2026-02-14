using System.Collections.ObjectModel;
using System.Windows.Input;
using MMV.App.Commands;
using MMV.App.Services;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel pour la liste des utilisateurs avec recherche, filtrage et pagination.
/// </summary>
public class UsersListViewModel : BaseViewModel
{
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDialogService _dialogService;

    private string _searchText = string.Empty;
    private string _selectedRoleFilter = "Tous";
    private bool _showActiveOnly = true;
    private int _currentPage = 1;
    private int _totalPages = 1;
    private int _totalCount;
    private const int PageSize = 20;

    public ObservableCollection<User> Users { get; } = new();
    public ObservableCollection<User> FilteredUsers { get; } = new();
    public ObservableCollection<string> RoleFilters { get; } = new() { "Tous", "Admin", "Optician", "Technician" };

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                _currentPage = 1;
                ApplyFilter();
            }
        }
    }

    public string SelectedRoleFilter
    {
        get => _selectedRoleFilter;
        set
        {
            if (SetProperty(ref _selectedRoleFilter, value))
            {
                _currentPage = 1;
                ApplyFilter();
            }
        }
    }

    public bool ShowActiveOnly
    {
        get => _showActiveOnly;
        set
        {
            if (SetProperty(ref _showActiveOnly, value))
            {
                _currentPage = 1;
                ApplyFilter();
            }
        }
    }

    public int CurrentPage
    {
        get => _currentPage;
        set
        {
            if (SetProperty(ref _currentPage, value))
            {
                ApplyFilter();
                OnPropertyChanged(nameof(PageInfo));
            }
        }
    }

    public int TotalPages
    {
        get => _totalPages;
        set
        {
            if (SetProperty(ref _totalPages, value))
                OnPropertyChanged(nameof(PageInfo));
        }
    }

    public int TotalCount
    {
        get => _totalCount;
        set => SetProperty(ref _totalCount, value);
    }

    public string PageInfo => $"Page {CurrentPage} / {TotalPages} ({TotalCount} utilisateurs)";

    public ICommand CreateCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand ToggleActiveCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand PreviousPageCommand { get; }

    public event EventHandler? CreateUserRequested;
    public event EventHandler<User>? EditUserRequested;

    public UsersListViewModel(
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        IDialogService dialogService)
    {
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

        Title = "Liste des Utilisateurs";

        CreateCommand = new RelayCommand(() => CreateUserRequested?.Invoke(this, EventArgs.Empty));
        EditCommand = new RelayCommand<User>(user => { if (user != null) EditUserRequested?.Invoke(this, user); });
        ToggleActiveCommand = new RelayCommand<User>(async user => { if (user != null) await ToggleActiveAsync(user); });
        RefreshCommand = new RelayCommand(async () => await LoadUsersAsync());
        NextPageCommand = new RelayCommand(
            () => CurrentPage++,
            () => CurrentPage < TotalPages);
        PreviousPageCommand = new RelayCommand(
            () => CurrentPage--,
            () => CurrentPage > 1);

        _ = LoadUsersAsync();
    }

    public async Task LoadUsersAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var allUsers = await _userRepository.GetAllAsync();
            Users.Clear();
            foreach (var user in allUsers)
                Users.Add(user);

            ApplyFilter();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur lors du chargement des utilisateurs : {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[UsersListVM] Erreur : {ex}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyFilter()
    {
        var filtered = Users.AsEnumerable();

        // Filtre par recherche
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText.ToLowerInvariant();
            filtered = filtered.Where(u =>
                u.Username.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                u.FirstName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                u.LastName.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        // Filtre par rôle
        if (SelectedRoleFilter != "Tous" && Enum.TryParse<UserRole>(SelectedRoleFilter, out var role))
        {
            filtered = filtered.Where(u => u.Role == role);
        }

        // Filtre actifs uniquement
        if (ShowActiveOnly)
        {
            filtered = filtered.Where(u => u.IsActive);
        }

        var filteredList = filtered.OrderBy(u => u.LastName).ThenBy(u => u.FirstName).ToList();
        TotalCount = filteredList.Count;
        TotalPages = Math.Max(1, (int)Math.Ceiling((double)TotalCount / PageSize));

        if (CurrentPage > TotalPages)
            _currentPage = TotalPages;

        var paged = filteredList
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize)
            .ToList();

        FilteredUsers.Clear();
        foreach (var user in paged)
            FilteredUsers.Add(user);

        OnPropertyChanged(nameof(PageInfo));
        ((RelayCommand)NextPageCommand).RaiseCanExecuteChanged();
        ((RelayCommand)PreviousPageCommand).RaiseCanExecuteChanged();
    }

    private async Task ToggleActiveAsync(User user)
    {
        var action = user.IsActive ? "désactiver" : "activer";
        var confirm = await _dialogService.ShowConfirmationAsync(
            $"Confirmation",
            $"Voulez-vous {action} l'utilisateur '{user.Username}' ({user.FirstName} {user.LastName}) ?");

        if (!confirm) return;

        try
        {
            user.IsActive = !user.IsActive;
            await _userRepository.UpdateAsync(user);
            await _unitOfWork.SaveChangesAsync();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            // Rétablir en cas d'erreur
            user.IsActive = !user.IsActive;
            await _dialogService.ShowErrorAsync("Erreur", $"Impossible de {action} l'utilisateur : {ex.Message}");
        }
    }
}
