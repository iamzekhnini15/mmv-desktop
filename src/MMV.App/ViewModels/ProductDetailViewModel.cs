using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using MMV.App.Commands;
using MMV.Domain.Entities;

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

    public ICommand BackCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand DeleteCommand { get; }

    public event EventHandler? BackRequested;
    public event EventHandler<Product>? EditRequested;
    public event EventHandler<Product>? DeleteRequested;

    public ProductDetailViewModel()
    {
        BackCommand = new RelayCommand(ExecuteBack);
        EditCommand = new RelayCommand(ExecuteEdit, CanEditOrDelete);
        DeleteCommand = new RelayCommand(ExecuteDelete, CanEditOrDelete);

        Title = "Fiche Produit";
    }

    public void Initialize(Product product)
    {
        Product = product;
        Title = $"Fiche - {product.Name}";
        LoadOrderHistory();
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
