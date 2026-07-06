using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using MMV.App.Commands;
using MMV.Application.UseCases.Suppliers.GetSupplierWithProducts;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel pour la fiche détaillée d'un fournisseur.
/// <para>
/// P2D-2 : consomme un <see cref="SupplierDetailsDto"/> applicatif (et ses <see cref="SupplierProductItemDto"/>)
/// au lieu de l'entité EF <c>Supplier</c> et de sa navigation <c>Products</c>.
/// </para>
/// </summary>
public class SupplierDetailViewModel : BaseViewModel
{
    private SupplierDetailsDto? _supplier;
    private ObservableCollection<SupplierProductItemDto> _products = new();
    private RelayCommand? _editCommand;
    private RelayCommand? _deleteCommand;

    public SupplierDetailsDto? Supplier
    {
        get => _supplier;
        set => SetProperty(ref _supplier, value);
    }

    public ObservableCollection<SupplierProductItemDto> Products
    {
        get => _products;
        set => SetProperty(ref _products, value);
    }

    public bool HasProducts => Products.Count > 0;

    public ICommand BackCommand { get; }
    public ICommand EditCommand => _editCommand ??= new RelayCommand(ExecuteEdit, CanEditOrDelete);
    public ICommand DeleteCommand => _deleteCommand ??= new RelayCommand(ExecuteDelete, CanEditOrDelete);

    public event EventHandler? BackRequested;
    public event EventHandler<SupplierDetailsDto>? EditRequested;
    public event EventHandler<SupplierDetailsDto>? DeleteRequested;

    public SupplierDetailViewModel()
    {
        BackCommand = new RelayCommand(ExecuteBack);
        Title = "Fiche Fournisseur";
    }

    public void Initialize(SupplierDetailsDto supplier)
    {
        Supplier = supplier;
        Title = $"Fiche - {supplier.Name}";

        Products.Clear();
        if (supplier.Products != null)
        {
            foreach (var product in supplier.Products.OrderBy(p => p.Name))
            {
                Products.Add(product);
            }
        }

        _editCommand?.RaiseCanExecuteChanged();
        _deleteCommand?.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(HasProducts));
    }

    private bool CanEditOrDelete()
    {
        return Supplier != null && Supplier.SupplierId > 0;
    }

    private void ExecuteBack()
    {
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ExecuteEdit()
    {
        if (Supplier != null)
        {
            EditRequested?.Invoke(this, Supplier);
        }
    }

    private void ExecuteDelete()
    {
        if (Supplier != null)
        {
            DeleteRequested?.Invoke(this, Supplier);
        }
    }
}
