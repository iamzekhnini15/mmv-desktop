using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using MMV.App.Commands;
using MMV.Application.UseCases.Suppliers.ListSuppliers;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel pour la liste des fournisseurs.
/// <para>
/// P2D-2 : la lecture passe par <see cref="IListSuppliersUseCase"/> (query use case renvoyant des
/// <see cref="SupplierListItemDto"/> applicatifs) ; plus aucune dépendance <c>ISupplierRepository</c>.
/// </para>
/// </summary>
public class SuppliersListViewModel : BaseViewModel
{
    private readonly IListSuppliersUseCase _listSuppliersUseCase;

    private ObservableCollection<SupplierListItemDto> _suppliers;
    private ObservableCollection<SupplierListItemDto> _filteredSuppliers;
    private SupplierListItemDto? _selectedSupplier;
    private string _searchText = string.Empty;
    private int _currentPage = 1;
    private int _pageSize = 20;
    private int _totalSuppliers;

    private ICommand? _createCommand;
    private ICommand? _refreshCommand;
    private ICommand? _viewDetailsCommand;
    private ICommand? _nextPageCommand;
    private ICommand? _previousPageCommand;

    public ObservableCollection<SupplierListItemDto> Suppliers
    {
        get => _suppliers;
        set => SetProperty(ref _suppliers, value);
    }

    public ObservableCollection<SupplierListItemDto> FilteredSuppliers
    {
        get => _filteredSuppliers;
        set => SetProperty(ref _filteredSuppliers, value);
    }

    public SupplierListItemDto? SelectedSupplier
    {
        get => _selectedSupplier;
        set
        {
            if (SetProperty(ref _selectedSupplier, value))
            {
                (ViewDetailsCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                ApplyFilter();
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
                OnPropertyChanged(nameof(CanGoToPreviousPage));
                OnPropertyChanged(nameof(CanGoToNextPage));
            }
        }
    }

    public int PageSize
    {
        get => _pageSize;
        set => SetProperty(ref _pageSize, value);
    }

    public int TotalSuppliers
    {
        get => _totalSuppliers;
        set
        {
            if (SetProperty(ref _totalSuppliers, value))
                OnPropertyChanged(nameof(TotalPages));
        }
    }

    public int TotalPages => (int)Math.Ceiling((double)TotalSuppliers / PageSize);

    public int FilteredCount => FilteredSuppliers?.Count ?? 0;

    public bool CanGoToPreviousPage => CurrentPage > 1;
    public bool CanGoToNextPage => CurrentPage < TotalPages;

    public ICommand CreateCommand => _createCommand ??= new RelayCommand(ExecuteCreate);
    public ICommand RefreshCommand => _refreshCommand ??= new RelayCommand(async () => await LoadSuppliersAsync());
    public ICommand ViewDetailsCommand => _viewDetailsCommand ??= new RelayCommand(ExecuteViewDetails, CanViewDetails);
    public ICommand NextPageCommand => _nextPageCommand ??= new RelayCommand(ExecuteNextPage, () => CanGoToNextPage);
    public ICommand PreviousPageCommand => _previousPageCommand ??= new RelayCommand(ExecutePreviousPage, () => CanGoToPreviousPage);

    public event EventHandler? CreateSupplierRequested;
    public event EventHandler<SupplierListItemDto>? ViewSupplierDetailsRequested;

    // P2D-2 : la liste consomme un query use case Application (IListSuppliersUseCase) renvoyant des DTO ;
    // plus aucune dépendance ISupplierRepository (ni IUnitOfWork, retiré en P2C-GLOBAL comme dépendance morte).
    public SuppliersListViewModel(IListSuppliersUseCase listSuppliersUseCase)
    {
        _listSuppliersUseCase = listSuppliersUseCase ?? throw new ArgumentNullException(nameof(listSuppliersUseCase));

        _suppliers = new ObservableCollection<SupplierListItemDto>();
        _filteredSuppliers = new ObservableCollection<SupplierListItemDto>();

        _ = LoadSuppliersAsync();
    }

    public async Task LoadSuppliersAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            var suppliers = await _listSuppliersUseCase.ExecuteAsync(new ListSuppliersQuery());
            Suppliers.Clear();
            foreach (var supplier in suppliers)
            {
                Suppliers.Add(supplier);
            }

            TotalSuppliers = Suppliers.Count;
            ApplyFilter();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur lors du chargement des fournisseurs : {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyFilter()
    {
        if (Suppliers == null) return;

        var filtered = Suppliers.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText.ToLower();
            filtered = filtered.Where(s =>
                (s.Name?.ToLower().Contains(search) ?? false) ||
                (s.ContactEmail?.ToLower().Contains(search) ?? false) ||
                (s.Phone?.ToLower().Contains(search) ?? false) ||
                (s.ReferenceCode?.ToLower().Contains(search) ?? false) ||
                (s.Address?.ToLower().Contains(search) ?? false));
        }

        var paginatedResults = filtered
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize)
            .ToList();

        FilteredSuppliers.Clear();
        foreach (var supplier in paginatedResults)
        {
            FilteredSuppliers.Add(supplier);
        }

        OnPropertyChanged(nameof(FilteredCount));
        (NextPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (PreviousPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void ExecuteCreate()
    {
        CreateSupplierRequested?.Invoke(this, EventArgs.Empty);
    }

    private bool CanViewDetails()
    {
        return SelectedSupplier != null;
    }

    private void ExecuteViewDetails()
    {
        if (SelectedSupplier != null)
        {
            ViewSupplierDetailsRequested?.Invoke(this, SelectedSupplier);
        }
    }

    private void ExecuteNextPage()
    {
        if (CanGoToNextPage)
        {
            CurrentPage++;
        }
    }

    private void ExecutePreviousPage()
    {
        if (CanGoToPreviousPage)
        {
            CurrentPage--;
        }
    }
}
