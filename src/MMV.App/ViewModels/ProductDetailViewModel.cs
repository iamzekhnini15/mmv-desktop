using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using MMV.App.Commands;
using MMV.Domain.Entities;
using MMV.Domain.Enums;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel pour la fiche détaillée d'un produit.
/// </summary>
public class ProductDetailViewModel : BaseViewModel
{
    private Product? _product;
    private ObservableCollection<OrderItem> _orderHistory = new();

    /// <summary>
    /// Produit en cours de consultation.
    /// </summary>
    public Product? Product
    {
        get => _product;
        set => SetProperty(ref _product, value);
    }

    /// <summary>
    /// Historique des commandes contenant ce produit.
    /// </summary>
    public ObservableCollection<OrderItem> OrderHistory
    {
        get => _orderHistory;
        set => SetProperty(ref _orderHistory, value);
    }

    /// <summary>
    /// Indicates if the current product is a glass/verre.
    /// </summary>
    public bool IsGlassCategory => Product?.Category == ProductCategoryEnum.VERRE;

    /// <summary>
    /// Indicates if the current product is a lens/lentille.
    /// </summary>
    public bool IsLensCategory => Product?.Category == ProductCategoryEnum.LENTILLE;

    /// <summary>
    /// Indicates if the current product is an accessory (monture, clips, plastic, solaire).
    /// </summary>
    public bool IsAccessoryCategory => Product?.Category == ProductCategoryEnum.MONTURE ||
                                       Product?.Category == ProductCategoryEnum.CLIPS ||
                                       Product?.Category == ProductCategoryEnum.PLASTIC ||
                                       Product?.Category == ProductCategoryEnum.SOLAIRE;

    private RelayCommand? _editCommand;
    private RelayCommand? _deleteCommand;

    public ICommand BackCommand { get; }
    public ICommand EditCommand => _editCommand ??= new RelayCommand(ExecuteEdit, CanEditOrDelete);
    public ICommand DeleteCommand => _deleteCommand ??= new RelayCommand(ExecuteDelete, CanEditOrDelete);

    public event EventHandler? BackRequested;
    public event EventHandler<Product>? EditRequested;
    public event EventHandler<Product>? DeleteRequested;

    public ProductDetailViewModel()
    {
        BackCommand = new RelayCommand(ExecuteBack);
        Title = "Fiche Produit";
    }

    public void Initialize(Product product)
    {
        Product = product;
        Title = $"Fiche - {product.Name}";
        LoadOrderHistory();
        OnPropertyChanged(nameof(IsGlassCategory));
        OnPropertyChanged(nameof(IsLensCategory));
        OnPropertyChanged(nameof(IsAccessoryCategory));
        
        // Notifier les commandes que le produit a changé
        _editCommand?.RaiseCanExecuteChanged();
        _deleteCommand?.RaiseCanExecuteChanged();
    }

    private void LoadOrderHistory()
    {
        if (Product?.OrderItems != null)
        {
            var sortedOrders = Product.OrderItems
                .OrderByDescending(oi => oi.Order?.OrderDate ?? DateTime.MinValue)
                .ToList();
            
            OrderHistory.Clear();
            foreach (var item in sortedOrders)
            {
                OrderHistory.Add(item);
            }
        }
    }

    private bool CanEditOrDelete()
    {
        return Product != null && Product.ProductId > 0;
    }

    private void ExecuteBack()
    {
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ExecuteEdit()
    {
        if (Product != null)
        {
            EditRequested?.Invoke(this, Product);
        }
    }

    private void ExecuteDelete()
    {
        if (Product != null)
        {
            DeleteRequested?.Invoke(this, Product);
        }
    }
}
