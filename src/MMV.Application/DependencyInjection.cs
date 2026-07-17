using Microsoft.Extensions.DependencyInjection;
using MMV.Application.UseCases.Customers.CreateCustomer;
using MMV.Application.UseCases.Customers.DeleteCustomer;
using MMV.Application.UseCases.Customers.ListCustomers;
using MMV.Application.UseCases.Customers.ListCustomersForPicker;
using MMV.Application.UseCases.Customers.SetCustomerArchived;
using MMV.Application.UseCases.Customers.UpdateCustomer;
using MMV.Application.UseCases.Orders.AdvanceOrderStatus;
using MMV.Application.UseCases.Orders.CreateOrder;
using MMV.Application.UseCases.Orders.DeleteOrder;
using MMV.Application.UseCases.Orders.GetOrderDetails;
using MMV.Application.UseCases.Orders.ListOrders;
using MMV.Application.UseCases.Orders.SettleOrderBalance;
using MMV.Application.UseCases.Orders.UpdateOrder;
using MMV.Application.UseCases.Notifications.CountUnreadNotifications;
using MMV.Application.UseCases.Notifications.GenerateLowStockNotifications;
using MMV.Application.UseCases.Notifications.ListNotifications;
using MMV.Application.UseCases.Notifications.MarkAllNotificationsRead;
using MMV.Application.UseCases.Prescriptions.CreatePrescription;
using MMV.Application.UseCases.Prescriptions.DeletePrescription;
using MMV.Application.UseCases.Prescriptions.ListPrescriptionsByCustomer;
using MMV.Application.UseCases.Prescriptions.UpdatePrescription;
using MMV.Application.UseCases.Products.CreateProduct;
using MMV.Application.UseCases.Products.DeleteProduct;
using MMV.Application.UseCases.Products.GetInventoryOverview;
using MMV.Application.UseCases.Products.ListProducts;
using MMV.Application.UseCases.Products.ListProductsForOrderPicker;
using MMV.Application.UseCases.Products.ListProductsForPicker;
using MMV.Application.UseCases.Products.SetProductActive;
using MMV.Application.UseCases.Products.UpdateProduct;
using MMV.Application.UseCases.Sales.GetCustomerPurchaseHistory;
using MMV.Application.UseCases.Sales.GetSaleFormReferenceData;
using MMV.Application.UseCases.Sales.RegisterSale;
using MMV.Application.UseCases.Stock.CreateStockMovement;
using MMV.Application.UseCases.Stock.ListStockMovements;
using MMV.Application.UseCases.Suppliers.CreateSupplier;
using MMV.Application.UseCases.Suppliers.DeleteSupplier;
using MMV.Application.UseCases.Suppliers.GetSupplierWithProducts;
using MMV.Application.UseCases.Suppliers.ListSuppliers;
using MMV.Application.UseCases.Suppliers.UpdateSupplier;
using MMV.Application.UseCases.Users.CreateUser;
using MMV.Application.UseCases.Users.ListUsers;
using MMV.Application.UseCases.Users.SetUserActive;
using MMV.Application.UseCases.Users.UpdateUser;

namespace MMV.Application;

/// <summary>
/// Enregistrement DI de la couche Application. Appelée par le composition root unique
/// (<c>MMV.App</c> — <see cref="object">App.ConfigureServices</see>).
/// </summary>
/// <remarks>
/// <para>
/// P2B-2B : la couche Application est créée <b>à vide</b> (squelette). <see cref="AddApplication"/>
/// n'enregistre <b>aucun</b> use case métier réel pour l'instant.
/// </para>
/// <para>
/// Les use cases applicatifs (premier cible : <c>EnregistrerVente</c> / <c>RegisterSaleUseCase</c>)
/// seront enregistrés ici en <b>P2B-2C</b> lors du premier vertical slice, en portée <c>Scoped</c> —
/// la même portée que <c>OpticDbContext</c>, les repositories et le <c>ITransactionRunner</c> —
/// afin de partager la transaction (cf. plan de migration §4). La signature de cette méthode
/// reste stable : seuls des enregistrements s'y ajouteront.
/// </para>
/// </remarks>
public static class DependencyInjection
{
    /// <summary>
    /// Enregistre les services de la couche Application dans le conteneur d'injection de dépendances.
    /// Squelette neutre en P2B-2B (aucun use case réel) ; point d'extension pour P2B-2C+.
    /// </summary>
    /// <param name="services">Collection de services du composition root.</param>
    /// <returns>La même collection, pour chaînage.</returns>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Premier vertical slice (P2B-2C) — « Enregistrer une vente en magasin ». Portée Scoped : la même que
        // OpticDbContext, les repositories, ITransactionRunner, INumberSequenceService et IStockMutationService
        // — afin de partager le même DbContext, donc la même transaction (cf. plan de migration §4).
        services.AddScoped<IRegisterSaleUseCase, RegisterSaleUseCase>();

        // Deuxième vertical slice (P2B-2D) — « Créer une commande fournisseur ». Portée Scoped : même portée que
        // OpticDbContext, les repositories et IUnitOfWork — donc même DbContext (cohérent avec le flux d'origine).
        services.AddScoped<ICreateOrderUseCase, CreateOrderUseCase>();

        // Troisième vertical slice (P2B-2E) — « Faire avancer le statut / réception d'une commande ». Portée
        // Scoped : même portée que OpticDbContext, les repositories et IUnitOfWork — donc même DbContext, donc le
        // SaveChangesAsync unique reste atomique (cohérent avec le flux d'origine).
        services.AddScoped<IAdvanceOrderStatusUseCase, AdvanceOrderStatusUseCase>();

        // Quatrième vertical slice (P2B-2F) — « Créer un mouvement manuel de stock ». Portée Scoped : même portée
        // que OpticDbContext, les repositories, IUnitOfWork, ITransactionRunner et IStockMutationService — donc
        // même DbContext, donc la frontière transactionnelle (décrément/incrément + mouvement) reste atomique
        // (cohérent avec le flux d'origine P2A-1D-R2).
        services.AddScoped<ICreateStockMovementUseCase, CreateStockMovementUseCase>();

        // Cinquième vertical slice (P2B-2G) — « Encaisser le solde restant d'une commande ». Portée Scoped : même
        // portée que OpticDbContext, les repositories, IUnitOfWork et ITransactionRunner — donc même DbContext,
        // donc la frontière transactionnelle (mise à jour du paiement + notification) reste atomique (cohérent
        // avec le flux d'origine OrderDetailViewModel.ExecuteEncashBalanceAsync).
        services.AddScoped<ISettleOrderBalanceUseCase, SettleOrderBalanceUseCase>();

        // Sixième vertical slice (P2B-2H) — « Supprimer une commande ». Portée Scoped : même portée que
        // OpticDbContext, les repositories et IUnitOfWork — donc même DbContext. Mono-écriture (DeleteAsync +
        // SaveChangesAsync), ITransactionRunner non requis.
        services.AddScoped<IDeleteOrderUseCase, DeleteOrderUseCase>();

        // Septième vertical slice (P2B-2I) — « Modifier une commande existante ». Portée Scoped : même portée que
        // OpticDbContext, les repositories et IUnitOfWork — donc même DbContext (cohérent avec le flux d'origine).
        // Mono-écriture (UpdateAsync + SaveChangesAsync unique, reconstruction des lignes incluse via cascades EF),
        // ITransactionRunner non requis.
        services.AddScoped<IUpdateOrderUseCase, UpdateOrderUseCase>();

        // Première réduction de dette P2C (P2C-2) — flux client « create / update ». Portée Scoped : même portée
        // que OpticDbContext, ICustomerRepository et IUnitOfWork — donc même DbContext (cohérent avec le flux
        // d'origine porté par CustomerFormViewModel et le code-behind client). Mono-écriture (Create/Update +
        // SaveChangesAsync unique), ITransactionRunner non requis.
        services.AddScoped<ICreateCustomerUseCase, CreateCustomerUseCase>();
        services.AddScoped<IUpdateCustomerUseCase, UpdateCustomerUseCase>();

        // Reliquat client P2C-3 — flux « delete ». Portée Scoped : même portée que OpticDbContext,
        // ICustomerRepository et IUnitOfWork — donc même DbContext (cohérent avec le flux d'origine porté par
        // CustomersListViewModel.ExecuteDelete). Mono-écriture (Delete + SaveChangesAsync unique),
        // ITransactionRunner non requis.
        services.AddScoped<IDeleteCustomerUseCase, DeleteCustomerUseCase>();

        // Sécurisation métier P3-2B — archivage / réactivation client. Alternative non destructive à la
        // suppression (refusée dès qu'un historique existe). Portée Scoped : même portée que OpticDbContext,
        // ICustomerRepository et IUnitOfWork. Mono-écriture (Update + SaveChangesAsync unique),
        // ITransactionRunner non requis.
        services.AddScoped<ISetCustomerArchivedUseCase, SetCustomerArchivedUseCase>();

        // Réduction de dette P2C-4 — écritures du module Ordonnances. Portée Scoped : même portée que OpticDbContext,
        // IPrescriptionRepository et IUnitOfWork — donc même DbContext (cohérent avec le flux d'origine porté par
        // PrescriptionFormViewModel et CustomerPrescriptionsViewModel). Chaque écriture est mono-écriture
        // (Create/Update/Delete + SaveChangesAsync unique), ITransactionRunner non requis.
        // P3-3B : CreatePrescriptionUseCase dépend en plus d'ICustomerRepository (garde « client archivé » et refus
        // explicite du client introuvable) — résolu dans la même portée, donc le même DbContext.
        services.AddScoped<ICreatePrescriptionUseCase, CreatePrescriptionUseCase>();
        services.AddScoped<IUpdatePrescriptionUseCase, UpdatePrescriptionUseCase>();
        services.AddScoped<IDeletePrescriptionUseCase, DeletePrescriptionUseCase>();

        // Reliquat UI P2C-GLOBAL — écritures du module Fournisseurs. Portée Scoped : même portée que OpticDbContext,
        // ISupplierRepository et IUnitOfWork — donc même DbContext (cohérent avec le flux d'origine porté par
        // SupplierFormViewModel et SuppliersViewModel). Chaque écriture est mono-écriture (Create/Update/Delete +
        // SaveChangesAsync unique), ITransactionRunner non requis.
        services.AddScoped<ICreateSupplierUseCase, CreateSupplierUseCase>();
        services.AddScoped<IUpdateSupplierUseCase, UpdateSupplierUseCase>();
        services.AddScoped<IDeleteSupplierUseCase, DeleteSupplierUseCase>();

        // Reliquat UI P2C-GLOBAL — écritures du module Utilisateurs. Portée Scoped : même portée que OpticDbContext,
        // IUserRepository, IUnitOfWork et IAuthenticationService (hachage BCrypt) — donc même DbContext (cohérent
        // avec le flux d'origine porté par UserFormViewModel et UsersListViewModel). Chaque écriture est mono-écriture
        // (Create/Update + SaveChangesAsync unique), ITransactionRunner non requis.
        services.AddScoped<ICreateUserUseCase, CreateUserUseCase>();
        services.AddScoped<IUpdateUserUseCase, UpdateUserUseCase>();
        services.AddScoped<ISetUserActiveUseCase, SetUserActiveUseCase>();

        // Écritures du module Produits. Portée Scoped : même portée que OpticDbContext, IProductRepository,
        // IUnitOfWork et ITransactionRunner — donc même DbContext.
        // P3-4B : Create/Update enveloppent leur écriture dans ITransactionRunner afin qu'une violation d'unicité
        // concurrente sur la référence normalisée soit traduite en PersistenceException neutre (jamais un message
        // SQLite/EF brut) par le mécanisme existant PersistenceErrorMapper. Delete reste mono-écriture (garde
        // d'usage puis suppression). SetProductActive (désactivation/réactivation) remplace l'absence de
        // « suppression douce » constatée en P3-4A.
        services.AddScoped<ICreateProductUseCase, CreateProductUseCase>();
        services.AddScoped<IUpdateProductUseCase, UpdateProductUseCase>();
        services.AddScoped<IDeleteProductUseCase, DeleteProductUseCase>();
        services.AddScoped<ISetProductActiveUseCase, SetProductActiveUseCase>();

        // Reliquat UI P2C-GLOBAL — écritures du module Notifications (marquage global + génération stock bas). Portée
        // Scoped : même portée que OpticDbContext, INotificationRepository, IProductRepository et IUnitOfWork — donc
        // même DbContext. Écriture unique (SaveChangesAsync final), ITransactionRunner non requis. La génération de
        // stock bas était dupliquée entre NotificationsListViewModel et MainWindowViewModel : elle est unifiée ici.
        services.AddScoped<IMarkAllNotificationsReadUseCase, MarkAllNotificationsReadUseCase>();
        services.AddScoped<IGenerateLowStockNotificationsUseCase, GenerateLowStockNotificationsUseCase>();

        // ---------------------------------------------------------------------------------------------------------
        // P2D — Query use cases (LECTURES). Première famille de use cases *query* de la solution : ils remplacent les
        // lectures directes I…Repository qui subsistaient dans les ViewModels (dette ouverte à l'issue de P2C-GLOBAL).
        // Chaque query use case projette les entités vers des DTO applicatifs plats (lecture seule) : aucune entité EF
        // suivie ne franchit la frontière UI. Portée Scoped (même portée qu'OpticDbContext / repositories).
        // ---------------------------------------------------------------------------------------------------------

        // P2D-1 — module Utilisateurs : « Lister les utilisateurs » (remplace IUserRepository.GetAllAsync côté UI).
        services.AddScoped<IListUsersUseCase, ListUsersUseCase>();

        // P2D-2 — module Fournisseurs : liste + fiche détaillée (remplacent ISupplierRepository.GetAllAsync /
        // GetWithProductsAsync côté UI, dans SuppliersListViewModel, SuppliersViewModel et SupplierDetailViewModel).
        services.AddScoped<IListSuppliersUseCase, ListSuppliersUseCase>();
        services.AddScoped<IGetSupplierWithProductsUseCase, GetSupplierWithProductsUseCase>();

        // P2D-3 — module Notifications : liste + compteur non lus (remplacent INotificationRepository.GetAllAsync /
        // CountUnreadAsync côté UI, dans NotificationsListViewModel et NotificationsViewModel).
        services.AddScoped<IListNotificationsUseCase, ListNotificationsUseCase>();
        services.AddScoped<ICountUnreadNotificationsUseCase, CountUnreadNotificationsUseCase>();

        // P2D-4 — module Produits / Stock (lectures). Remplacent les lectures directes I…Repository restantes des
        // ViewModels de mouvements de stock, d'inventaire et des sélecteurs produit (StockMovementsListViewModel,
        // StockMovementFormViewModel, StockMovementsViewModel, InventoryViewModel). La lecture fournisseur du
        // formulaire produit (ProductFormViewModel) réutilise IListSuppliersUseCase (P2D-2). Portée Scoped (même
        // portée qu'OpticDbContext / repositories).
        services.AddScoped<IListStockMovementsUseCase, ListStockMovementsUseCase>();
        services.AddScoped<IListProductsForPickerUseCase, ListProductsForPickerUseCase>();
        services.AddScoped<IGetInventoryOverviewUseCase, GetInventoryOverviewUseCase>();

        // P2D-5 — module Clients / Ordonnances (lectures). Remplacent les lectures directes I…Repository des
        // ViewModels de lecture clients : historique d'achats (ISaleRepository.GetByCustomerIdAsync, consommé par
        // CustomerPurchaseHistoryViewModel et l'onglet Infos de CustomerInfoViewModel) et liste des ordonnances d'un
        // client (IPrescriptionRepository.GetByCustomerIdAsync, consommé par CustomerPrescriptionsViewModel). Portée
        // Scoped (même portée qu'OpticDbContext / repositories). Chaque query projette vers des DTO plats : aucune
        // entité EF suivie ne franchit la frontière UI.
        services.AddScoped<IGetCustomerPurchaseHistoryUseCase, GetCustomerPurchaseHistoryUseCase>();
        services.AddScoped<IListPrescriptionsByCustomerUseCase, ListPrescriptionsByCustomerUseCase>();

        // P2D-6 — module Commandes (lectures). Remplacent les lectures directes I…Repository restantes des ViewModels
        // de lecture Commandes : la liste + le Kanban (IOrderRepository.GetAllWithItemsAsync, consommé par
        // OrdersListViewModel et OrderKanbanViewModel → IListOrdersUseCase), et les données de référence du formulaire
        // de commande (ICustomerRepository.GetAllAsync → IListCustomersForPickerUseCase ; IProductRepository.GetAllAsync
        // → IListProductsForOrderPickerUseCase ; les ordonnances du client réutilisent IListPrescriptionsByCustomerUseCase,
        // P2D-5). Portée Scoped (même portée qu'OpticDbContext / repositories). Chaque query projette vers des DTO plats :
        // aucune entité EF suivie ne franchit la frontière UI. Les lectures de référence du formulaire de VENTE
        // (SaleFormViewModel : catalogue à graphe GlassDetail + panier d'entités OrderItem) restent en reliquat justifié
        // (cf. docs/implementation/P2D-6-report.md).
        services.AddScoped<IListOrdersUseCase, ListOrdersUseCase>();
        services.AddScoped<IListCustomersForPickerUseCase, ListCustomersForPickerUseCase>();
        services.AddScoped<IListProductsForOrderPickerUseCase, ListProductsForOrderPickerUseCase>();

        // P2D-7 — Clôture P2D (lectures restantes). Soldent les 11 dernières dépendances I…Repository de l'UI :
        // données de référence du formulaire de vente (catalogue + ordonnance active), fiche détaillée de commande
        // (threading vers le formulaire d'ÉDITION), liste clients (threading vers fiche + formulaire d'ÉDITION) et
        // liste produit à graphe (threading vers fiche + formulaire d'ÉDITION). Portée Scoped (même portée
        // qu'OpticDbContext / repositories). Chaque query projette vers des DTO plats/composites : aucune entité EF
        // suivie ne franchit la frontière UI.
        services.AddScoped<IGetSaleFormReferenceDataUseCase, GetSaleFormReferenceDataUseCase>();
        services.AddScoped<IGetOrderDetailsUseCase, GetOrderDetailsUseCase>();
        services.AddScoped<IListCustomersUseCase, ListCustomersUseCase>();
        services.AddScoped<IListProductsUseCase, ListProductsUseCase>();
        return services;
    }
}
