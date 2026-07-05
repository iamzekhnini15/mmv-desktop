using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using MMV.App.Commands;
using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel pour la liste des fournisseurs.
/// </summary>
public class SuppliersListViewModel : BaseViewModel
{
    private readonly ISupplierRepository _supplierRepository;

    private ObservableCollection<Supplier> _suppliers;
    private ObservableCollection<Supplier> _filteredSuppliers;
    private Supplier? _selectedSupplier;
    private string _searchText = string.Empty;
    private int _currentPage = 1;
    private int _pageSize = 20;
    private int _totalSuppliers;

    private ICommand? _createCommand;
    private ICommand? _refreshCommand;
    private ICommand? _viewDetailsCommand;
    private ICommand? _nextPageCommand;
    private ICommand? _previousPageCommand;

    public ObservableCollection<Supplier> Suppliers
    {
        get => _suppliers;
        set => SetProperty(ref _suppliers, value);
    }

    public ObservableCollection<Supplier> FilteredSuppliers
    {
        get => _filteredSuppliers;
        set => SetProperty(ref _filteredSuppliers, value);
    }

    public Supplier? SelectedSupplier
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
    public event EventHandler<Supplier>? ViewSupplierDetailsRequested;

    // P2C-GLOBAL : la liste ne consomme que ISupplierRepository (lectures d'affichage). Le paramètre IUnitOfWork
    // historiquement injecté n'était jamais utilisé (dépendance morte) et a été supprimé.
    public SuppliersListViewModel(ISupplierRepository supplierRepository)
    {
        _supplierRepository = supplierRepository;

        _suppliers = new ObservableCollection<Supplier>();
        _filteredSuppliers = new ObservableCollection<Supplier>();

        _ = LoadSuppliersAsync();
    }

    public async Task LoadSuppliersAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            var suppliers = await _supplierRepository.GetAllAsync() ?? Array.Empty<Supplier>();
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
