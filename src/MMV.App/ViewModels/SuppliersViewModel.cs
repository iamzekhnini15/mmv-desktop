using System;
using System.Windows.Input;
using MMV.App.Commands;
using MMV.App.Services;
using MMV.Application.UseCases.Suppliers.CreateSupplier;
using MMV.Application.UseCases.Suppliers.DeleteSupplier;
using MMV.Application.UseCases.Suppliers.UpdateSupplier;
using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel principal pour la gestion des fournisseurs.
/// <para>
/// P2C-GLOBAL : les écritures (suppression, ainsi que création/édition déléguées au formulaire) passent par la
/// couche Application. <see cref="ISupplierRepository"/> n'est conservé que pour les lectures d'affichage
/// (<c>GetWithProductsAsync</c> et alimentation de la liste). <c>IUnitOfWork</c> a été retiré.
/// </para>
/// </summary>
public class SuppliersViewModel : BaseViewModel
{
    private readonly ISupplierRepository _supplierRepository;
    private readonly ICreateSupplierUseCase _createSupplierUseCase;
    private readonly IUpdateSupplierUseCase _updateSupplierUseCase;
    private readonly IDeleteSupplierUseCase _deleteSupplierUseCase;
    private readonly IDialogService _dialogService;

    private SuppliersListViewModel _suppliersListViewModel;
    private SupplierFormViewModel? _supplierFormViewModel;
    private SupplierDetailViewModel? _supplierDetailViewModel;
    private bool _isInEditMode;
    private bool _isCreatingNew;
    private bool _isShowingDetail;
    private ICommand? _backToProductsCommand;

    public SuppliersListViewModel SuppliersListViewModel
    {
        get => _suppliersListViewModel;
        set => SetProperty(ref _suppliersListViewModel, value);
    }

    public SupplierFormViewModel? SupplierFormViewModel
    {
        get => _supplierFormViewModel;
        set => SetProperty(ref _supplierFormViewModel, value);
    }

    public SupplierDetailViewModel? SupplierDetailViewModel
    {
        get => _supplierDetailViewModel;
        set => SetProperty(ref _supplierDetailViewModel, value);
    }

    public bool IsInEditMode
    {
        get => _isInEditMode;
        set
        {
            if (SetProperty(ref _isInEditMode, value))
                OnPropertyChanged(nameof(ShowList));
        }
    }

    public bool IsCreatingNew
    {
        get => _isCreatingNew;
        set => SetProperty(ref _isCreatingNew, value);
    }

    public bool IsShowingDetail
    {
        get => _isShowingDetail;
        set
        {
            if (SetProperty(ref _isShowingDetail, value))
                OnPropertyChanged(nameof(ShowList));
        }
    }

    public bool ShowList => !IsInEditMode && !IsShowingDetail;

    public ICommand BackToProductsCommand => _backToProductsCommand ??= new RelayCommand(ExecuteBackToProducts);

    public event EventHandler? BackToProductsRequested;

    public SuppliersViewModel(
        ISupplierRepository supplierRepository,
        ICreateSupplierUseCase createSupplierUseCase,
        IUpdateSupplierUseCase updateSupplierUseCase,
        IDeleteSupplierUseCase deleteSupplierUseCase,
        IDialogService dialogService)
    {
        _supplierRepository = supplierRepository;
        _createSupplierUseCase = createSupplierUseCase ?? throw new ArgumentNullException(nameof(createSupplierUseCase));
        _updateSupplierUseCase = updateSupplierUseCase ?? throw new ArgumentNullException(nameof(updateSupplierUseCase));
        _deleteSupplierUseCase = deleteSupplierUseCase ?? throw new ArgumentNullException(nameof(deleteSupplierUseCase));
        _dialogService = dialogService;

        _suppliersListViewModel = new SuppliersListViewModel(supplierRepository);
        _suppliersListViewModel.CreateSupplierRequested += OnCreateSupplierRequested;
        _suppliersListViewModel.ViewSupplierDetailsRequested += OnViewSupplierDetailsRequested;

        Title = "Gestion des Fournisseurs";
    }

    private void ExecuteBackToProducts()
    {
        BackToProductsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnCreateSupplierRequested(object? sender, EventArgs e)
    {
        SupplierFormViewModel = new SupplierFormViewModel(_createSupplierUseCase, _updateSupplierUseCase);
        SupplierFormViewModel.InitializeForCreate();
        SupplierFormViewModel.SupplierSaved += OnSupplierSaved;
        SupplierFormViewModel.Cancelled += OnSupplierFormCancelled;

        IsInEditMode = true;
        IsCreatingNew = true;
    }

    private async void OnViewSupplierDetailsRequested(object? sender, Supplier supplier)
    {
        var supplierWithProducts = await _supplierRepository.GetWithProductsAsync(supplier.SupplierId) ?? supplier;

        SupplierDetailViewModel = new SupplierDetailViewModel();
        SupplierDetailViewModel.Initialize(supplierWithProducts);
        SupplierDetailViewModel.BackRequested += OnDetailBackRequested;
        SupplierDetailViewModel.EditRequested += OnDetailEditRequested;
        SupplierDetailViewModel.DeleteRequested += OnDetailDeleteRequested;

        IsShowingDetail = true;
        IsInEditMode = false;
    }

    private void OnDetailBackRequested(object? sender, EventArgs e)
    {
        CloseDetail();
    }

    private void OnDetailEditRequested(object? sender, Supplier supplier)
    {
        CloseDetail();

        SupplierFormViewModel = new SupplierFormViewModel(_createSupplierUseCase, _updateSupplierUseCase);
        SupplierFormViewModel.InitializeForEdit(supplier);
        SupplierFormViewModel.SupplierSaved += OnSupplierSaved;
        SupplierFormViewModel.Cancelled += OnSupplierFormCancelled;

        IsInEditMode = true;
        IsCreatingNew = false;
    }

    private async void OnDetailDeleteRequested(object? sender, Supplier supplier)
    {
        if (supplier == null) return;

        var message = $"Êtes-vous sûr de vouloir supprimer le fournisseur :\n\n" +
                     $"🏢 {supplier.Name}\n" +
                     $"📧 {supplier.ContactEmail}\n" +
                     $"📞 {supplier.Phone}\n\n" +
                     $"⚠️ Cette action est irréversible !";

        try
        {
            var confirmed = await _dialogService.ShowConfirmationAsync(
                "Confirmation de suppression",
                message);

            if (confirmed)
            {
                await _deleteSupplierUseCase.ExecuteAsync(new DeleteSupplierCommand { SupplierId = supplier.SupplierId });
                await SuppliersListViewModel.LoadSuppliersAsync();
                CloseDetail();
            }
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync(
                "Erreur de suppression",
                $"Impossible de supprimer le fournisseur :\n{ex.Message}");
        }
    }

    private async void OnSupplierSaved(object? sender, Supplier supplier)
    {
        await SuppliersListViewModel.LoadSuppliersAsync();
        CloseForm();
    }

    private void OnSupplierFormCancelled(object? sender, EventArgs e)
    {
        CloseForm();
    }

    public void CloseDetail()
    {
        IsShowingDetail = false;
        SupplierDetailViewModel = null;
    }

    public void CloseForm()
    {
        IsInEditMode = false;
        SupplierFormViewModel = null;
    }
}
