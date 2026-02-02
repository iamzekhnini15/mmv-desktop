using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using MMV.App.Commands;
using MMV.Domain.Entities;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel pour la fiche détaillée d'un fournisseur.
/// </summary>
public class SupplierDetailViewModel : BaseViewModel
{
    private Supplier? _supplier;
    private ObservableCollection<Product> _products = new();
    private RelayCommand? _editCommand;
    private RelayCommand? _deleteCommand;

    public Supplier? Supplier
    {
        get => _supplier;
        set => SetProperty(ref _supplier, value);
    }

    public ObservableCollection<Product> Products
    {
        get => _products;
        set => SetProperty(ref _products, value);
    }

    public bool HasProducts => Products.Count > 0;

    public ICommand BackCommand { get; }
    public ICommand EditCommand => _editCommand ??= new RelayCommand(ExecuteEdit, CanEditOrDelete);
    public ICommand DeleteCommand => _deleteCommand ??= new RelayCommand(ExecuteDelete, CanEditOrDelete);

    public event EventHandler? BackRequested;
    public event EventHandler<Supplier>? EditRequested;
    public event EventHandler<Supplier>? DeleteRequested;

    public SupplierDetailViewModel()
    {
        BackCommand = new RelayCommand(ExecuteBack);
        Title = "Fiche Fournisseur";
    }

    public void Initialize(Supplier supplier)
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
