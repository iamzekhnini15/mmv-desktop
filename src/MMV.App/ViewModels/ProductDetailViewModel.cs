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
    /// Calcul de la marge en euros (vente - achat) pour affichage.
    /// </summary>
    public decimal Margin => (Product?.SalePrice ?? 0m) - (Product?.PurchasePrice ?? 0m);

    /// <summary>
    /// Calcul de la marge en pourcentage pour affichage.
    /// </summary>
    public string MarginPercentage
    {
        get
        {
            var purchase = Product?.PurchasePrice ?? 0m;
            var sale = Product?.SalePrice ?? 0m;
            if (purchase == 0m) return "0%";
            var perc = ((sale - purchase) / purchase) * 100m;
            return $"{perc:F1}%";
        }
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

    // Propriétés calculées pour les détails du produit (conversion enum -> string)
    
    // GlassDetail properties
    public string? GlassMaterial => Product?.GlassDetail?.Material?.ToString();
    public string? GlassType => Product?.GlassDetail?.GlassType?.ToString();
    public string? GlassDiameter => Product?.GlassDetail?.Diameter;
    public string? GlassIndex => Product?.GlassDetail?.Index?.ToString("F2");
    public string? GlassPowerLimitMin => Product?.GlassDetail?.PowerLimitMin?.ToString("F2");
    public string? GlassPowerLimitMax => Product?.GlassDetail?.PowerLimitMax?.ToString("F2");

    // LensDetail properties
    public string? LensBrand => Product?.LensDetail?.Brand;
    public string? LensModel => Product?.LensDetail?.Model;
    public string? LensMaterial => Product?.LensDetail?.Material?.ToString();
    public string? LensType => Product?.LensDetail?.LensType?.ToString();
    public string? LensDuration => Product?.LensDetail?.Duration?.ToString();
    public string? LensDiameter => Product?.LensDetail?.Diameter?.ToString("F1");
    public string? LensBaseCurve => Product?.LensDetail?.BaseCurve?.ToString("F1");

    // AccessoryDetail properties
    public string? AccessoryColor => Product?.AccessoryDetail?.Color;
    public string? AccessorySize => Product?.AccessoryDetail?.Size;
    public string? AccessoryMaterial => Product?.AccessoryDetail?.Material;

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
        OnPropertyChanged(nameof(Margin));
        OnPropertyChanged(nameof(MarginPercentage));
        
        // Notifier les propriétés calculées des détails
        OnPropertyChanged(nameof(GlassMaterial));
        OnPropertyChanged(nameof(GlassType));
        OnPropertyChanged(nameof(GlassDiameter));
        OnPropertyChanged(nameof(GlassIndex));
        OnPropertyChanged(nameof(GlassPowerLimitMin));
        OnPropertyChanged(nameof(GlassPowerLimitMax));
        OnPropertyChanged(nameof(LensBrand));
        OnPropertyChanged(nameof(LensModel));
        OnPropertyChanged(nameof(LensMaterial));
        OnPropertyChanged(nameof(LensType));
        OnPropertyChanged(nameof(LensDuration));
        OnPropertyChanged(nameof(LensDiameter));
        OnPropertyChanged(nameof(LensBaseCurve));
        OnPropertyChanged(nameof(AccessoryColor));
        OnPropertyChanged(nameof(AccessorySize));
        OnPropertyChanged(nameof(AccessoryMaterial));
        
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
