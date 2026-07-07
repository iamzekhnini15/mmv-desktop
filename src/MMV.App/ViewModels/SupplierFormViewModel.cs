using System;
using System.Threading.Tasks;
using MMV.App.Commands;
using MMV.Application.UseCases.Suppliers.CreateSupplier;
using MMV.Application.UseCases.Suppliers.GetSupplierWithProducts;
using MMV.Application.UseCases.Suppliers.UpdateSupplier;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel pour le formulaire de création/édition d'un fournisseur.
/// <para>
/// P2C-GLOBAL : la persistance directe (repository + <c>IUnitOfWork</c> + <c>SaveChangesAsync</c>) a été déplacée
/// vers la couche Application (<see cref="ICreateSupplierUseCase"/> / <see cref="IUpdateSupplierUseCase"/>). La
/// ViewModel ne conserve que l'état d'écran, la validation de surface et la délégation aux use cases.
/// </para>
/// </summary>
public class SupplierFormViewModel : BaseViewModel
{
    private readonly ICreateSupplierUseCase _createSupplierUseCase;
    private readonly IUpdateSupplierUseCase _updateSupplierUseCase;
    private long? _editingSupplierId;

    private string _name = string.Empty;
    private string? _contactEmail;
    private string? _phone;
    private string? _address;
    private string? _referenceCode;
    private bool _isEditMode;
    private bool _isSaving;

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                SaveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string? ContactEmail
    {
        get => _contactEmail;
        set => SetProperty(ref _contactEmail, value);
    }

    public string? Phone
    {
        get => _phone;
        set => SetProperty(ref _phone, value);
    }

    public string? Address
    {
        get => _address;
        set => SetProperty(ref _address, value);
    }

    public string? ReferenceCode
    {
        get => _referenceCode;
        set => SetProperty(ref _referenceCode, value);
    }

    public bool IsEditMode
    {
        get => _isEditMode;
        private set => SetProperty(ref _isEditMode, value);
    }

    public bool IsSaving
    {
        get => _isSaving;
        set
        {
            if (SetProperty(ref _isSaving, value))
            {
                SaveCommand.RaiseCanExecuteChanged();
                CancelCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public RelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }

    public event EventHandler? SupplierSaved;
    public event EventHandler? Cancelled;

    public SupplierFormViewModel(ICreateSupplierUseCase createSupplierUseCase, IUpdateSupplierUseCase updateSupplierUseCase)
    {
        _createSupplierUseCase = createSupplierUseCase ?? throw new ArgumentNullException(nameof(createSupplierUseCase));
        _updateSupplierUseCase = updateSupplierUseCase ?? throw new ArgumentNullException(nameof(updateSupplierUseCase));

        SaveCommand = new RelayCommand(async () => await SaveAsync(), CanSave);
        CancelCommand = new RelayCommand(ExecuteCancel, () => !IsSaving);

        Title = "Nouveau Fournisseur";
    }

    public void InitializeForCreate()
    {
        _editingSupplierId = null;
        IsEditMode = false;
        Title = "Nouveau Fournisseur";

        Name = string.Empty;
        ContactEmail = string.Empty;
        Phone = string.Empty;
        Address = string.Empty;
        ReferenceCode = string.Empty;
    }

    public void InitializeForEdit(SupplierDetailsDto supplier)
    {
        ArgumentNullException.ThrowIfNull(supplier);

        _editingSupplierId = supplier.SupplierId;
        IsEditMode = true;
        Title = $"Modifier - {supplier.Name}";

        Name = supplier.Name;
        ContactEmail = supplier.ContactEmail;
        Phone = supplier.Phone;
        Address = supplier.Address;
        ReferenceCode = supplier.ReferenceCode;
    }

    private bool CanSave()
    {
        return !IsSaving && !string.IsNullOrWhiteSpace(Name);
    }

    private async Task SaveAsync()
    {
        if (!CanSave()) return;

        IsSaving = true;

        try
        {
            if (IsEditMode && _editingSupplierId is { } supplierId)
            {
                await _updateSupplierUseCase.ExecuteAsync(new UpdateSupplierCommand
                {
                    SupplierId = supplierId,
                    Name = Name,
                    ContactEmail = ContactEmail,
                    Phone = Phone,
                    Address = Address,
                    ReferenceCode = ReferenceCode
                });
            }
            else
            {
                await _createSupplierUseCase.ExecuteAsync(new CreateSupplierCommand
                {
                    Name = Name,
                    ContactEmail = ContactEmail,
                    Phone = Phone,
                    Address = Address,
                    ReferenceCode = ReferenceCode
                });
            }

            // Le parent (SuppliersViewModel) recharge la liste depuis le query use case : l'événement ne
            // transporte plus d'entité (aucune entité EF ne franchit la frontière UI).
            SupplierSaved?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsSaving = false;
        }
    }

    private void ExecuteCancel()
    {
        if (!IsSaving)
        {
            Cancelled?.Invoke(this, EventArgs.Empty);
        }
    }
}
