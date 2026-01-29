using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using MMV.App.Commands;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel pour le formulaire de création d'une vente.
/// </summary>
public class SaleFormViewModel : BaseViewModel
{
    private readonly ISaleRepository? _saleRepository;
    private readonly IUnitOfWork? _unitOfWork;

    private long _customerId;
    private decimal _totalAmount;
    private decimal _discountAmount = 0;
    private PaymentMethod _paymentMethod = PaymentMethod.Cash;
    private string _notes = string.Empty;
    private bool _isSaving;
    private ObservableCollection<SaleItem> _saleItems = new();

    /// <summary>
    /// ID du client pour cette vente.
    /// </summary>
    public long CustomerId
    {
        get => _customerId;
        set => SetProperty(ref _customerId, value);
    }

    /// <summary>
    /// Montant total de la vente.
    /// </summary>
    public decimal TotalAmount
    {
        get => _totalAmount;
        set => SetProperty(ref _totalAmount, value);
    }

    /// <summary>
    /// Montant de la réduction.
    /// </summary>
    public decimal DiscountAmount
    {
        get => _discountAmount;
        set
        {
            if (SetProperty(ref _discountAmount, value))
            {
                CalculateFinalAmount();
            }
        }
    }

    /// <summary>
    /// Méthode de paiement.
    /// </summary>
    public PaymentMethod PaymentMethod
    {
        get => _paymentMethod;
        set => SetProperty(ref _paymentMethod, value);
    }

    /// <summary>
    /// Notes supplémentaires.
    /// </summary>
    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    /// <summary>
    /// Indique si la vente est en cours de sauvegarde.
    /// </summary>
    public bool IsSaving
    {
        get => _isSaving;
        set => SetProperty(ref _isSaving, value);
    }

    /// <summary>
    /// Éléments de la vente.
    /// </summary>
    public ObservableCollection<SaleItem> SaleItems
    {
        get => _saleItems;
        set => SetProperty(ref _saleItems, value);
    }

    /// <summary>
    /// Commande pour sauvegarder la vente.
    /// </summary>
    public ICommand SaveCommand { get; }

    /// <summary>
    /// Commande pour annuler.
    /// </summary>
    public ICommand CancelCommand { get; }

    /// <summary>
    /// Événement déclenché quand la vente est sauvegardée.
    /// </summary>
    public event EventHandler<Sale>? SaleSaved;

    /// <summary>
    /// Événement déclenché quand on annule.
    /// </summary>
    public event EventHandler? Cancelled;

    public SaleFormViewModel(ISaleRepository? saleRepository, IUnitOfWork? unitOfWork)
    {
        _saleRepository = saleRepository;
        _unitOfWork = unitOfWork;

        SaveCommand = new RelayCommand(ExecuteSave);
        CancelCommand = new RelayCommand(ExecuteCancel);

        Title = "Nouvelle Vente";
    }

    /// <summary>
    /// Ajoute un article à la vente.
    /// </summary>
    public void AddSaleItem(SaleItem item)
    {
        SaleItems.Add(item);
        CalculateFinalAmount();
    }

    /// <summary>
    /// Supprime un article de la vente.
    /// </summary>
    public void RemoveSaleItem(SaleItem item)
    {
        SaleItems.Remove(item);
        CalculateFinalAmount();
    }

    /// <summary>
    /// Calcule le montant final de la vente (TotalAmount - DiscountAmount).
    /// </summary>
    private void CalculateFinalAmount()
    {
        // Le montant final est = montant total - montant de la remise
        // (Cette logique sera finalisée dans le repository)
    }

    private void ExecuteSave()
    {
        if (_saleRepository == null || _unitOfWork == null)
        {
            ErrorMessage = "Repository non initialisé";
            return;
        }

        IsSaving = true;
        try
        {
            // Calculer le montant total des articles
            decimal subtotal = 0;
            foreach (var item in SaleItems)
            {
                subtotal += item.UnitPrice * item.Quantity;
            }

            // Créer la vente
            var sale = new Sale
            {
                CustomerId = CustomerId,
                SaleDate = DateTime.Now,
                SaleNumber = $"VTE-{DateTime.Now:yyyy}-{new Random().Next(10000):D4}",
                TotalAmount = subtotal,
                DiscountAmount = DiscountAmount,
                FinalAmount = subtotal - DiscountAmount,
                PaymentMethod = PaymentMethod,
                PaymentStatus = PaymentStatus.Paid,
                Notes = Notes
            };

            // Ajouter les articles
            foreach (var item in SaleItems)
            {
                sale.SaleItems.Add(new SaleItem
                {
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice
                });
            }

            SaleSaved?.Invoke(this, sale);
            ClearForm();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur lors de la sauvegarde : {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    private void ExecuteCancel()
    {
        ClearForm();
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    private void ClearForm()
    {
        TotalAmount = 0;
        DiscountAmount = 0;
        PaymentMethod = PaymentMethod.Cash;
        Notes = string.Empty;
        SaleItems.Clear();
    }
}
