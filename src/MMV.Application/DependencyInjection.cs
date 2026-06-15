using Microsoft.Extensions.DependencyInjection;
using MMV.Application.UseCases.Customers.CreateCustomer;
using MMV.Application.UseCases.Customers.UpdateCustomer;
using MMV.Application.UseCases.Orders.AdvanceOrderStatus;
using MMV.Application.UseCases.Orders.CreateOrder;
using MMV.Application.UseCases.Orders.DeleteOrder;
using MMV.Application.UseCases.Orders.SettleOrderBalance;
using MMV.Application.UseCases.Orders.UpdateOrder;
using MMV.Application.UseCases.Sales.RegisterSale;
using MMV.Application.UseCases.Stock.CreateStockMovement;

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
        return services;
    }
}
