using Microsoft.Extensions.DependencyInjection;
using MMV.Application.UseCases.Orders.AdvanceOrderStatus;
using MMV.Application.UseCases.Orders.CreateOrder;
using MMV.Application.UseCases.Sales.RegisterSale;

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
        return services;
    }
}
