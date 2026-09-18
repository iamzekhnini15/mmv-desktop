using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MMV.Domain.Interfaces.Persistence;
using MMV.Domain.Interfaces.Repositories;
using MMV.Domain.Interfaces.Time;
using MMV.Domain.Services;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.Repositories;
using MMV.Infrastructure.Services;

namespace MMV.Infrastructure;

/// <summary>
/// Méthodes d'enregistrement des services d'infrastructure dans l'IoC container.
/// </summary>
/// <remarks>
/// P2B-2J (constat de nettoyage) : <see cref="AddInfrastructure"/> n'est <b>pas</b> appelée par le composition
/// root actuel. <c>MMV.App.App.ConfigureServices</c> enregistre directement le <c>DbContext</c>, les repositories
/// et les services techniques (cf. cycle de vie SQLite P2A-1A, qui pilote le chemin de base et la préparation).
/// Cette méthode est donc <b>inerte mais inoffensive</b> : elle n'a aucun effet de bord au démarrage et n'est
/// invoquée par aucun code de production ni de test. Elle est <b>volontairement conservée</b> comme point
/// d'extension réutilisable (composition root alternatif, intégration future) — sa suppression est hors périmètre
/// de cette phase de nettoyage (cf. rapport P2B-2J §8/§9).
/// </remarks>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration? configuration = null,
        string? connectionString = null)
    {
        var resolvedConnection = ResolveConnectionString(configuration, connectionString);

        services.AddDbContext<OpticDbContext>(options =>
        {
            options.UseSqlite(resolvedConnection);
        });

        // Repositories
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IProductCategoryRepository, ProductCategoryRepository>();
        services.AddScoped<ISupplierRepository, SupplierRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IPrescriptionRepository, PrescriptionRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<ISaleRepository, SaleRepository>();
        services.AddScoped<IStockMovementRepository, StockMovementRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Frontière transactionnelle réutilisable (P2A-1C, R-23) : partage le DbContext de la portée.
        services.AddScoped<ITransactionRunner, EfTransactionRunner>();

        // Décrément de stock atomique conditionnel (P2A-1D, R-09) : partage le DbContext de la portée,
        // s'exécute donc dans la transaction ouverte par le runner.
        services.AddScoped<IStockMutationService, EfStockMutationService>();

        // Numérotation fiable des documents (P2A-1E, R-03) : incrément atomique conditionnel d'un compteur
        // persistant ; partage le DbContext de la portée → participe à la transaction de la vente.
        services.AddScoped<INumberSequenceService, EfNumberSequenceService>();

        // Horloge injectable (P4-5D, ADR-PROD-DB-004 T1). SINGLETON — et non Scoped comme les repositories :
        // SystemClock est sans état, ne touche ni au DbContext ni à une transaction, et il ne doit exister
        // qu'UNE horloge dans le processus. L'instance enregistrée est celle exposée par SystemClock.Instance,
        // de sorte que les rares chemins non injectés (code-behind Avalonia) lisent la même horloge.
        services.AddSingleton<IClock>(SystemClock.Instance);

        // Services métier
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IProductService, ProductService>();
        // P3-3B : IPrescriptionService / PrescriptionService supprimés — second chemin d'écriture dormant, sans aucun
        // consommateur runtime, qui court-circuitait les use cases et donc leurs garde-fous (validation, client
        // archivé). L'écriture des ordonnances passe exclusivement par les use cases Application.
        // P3-6 : IOrderService / OrderService supprimés — service mort (aucun consommateur runtime) qui portait une
        // SECONDE matrice de transitions codée en dur. La matrice unique vit désormais dans OrderStatusPolicy (Domain).
        // P3-7 : ISaleService / SaleService supprimés — service mort (aucun consommateur runtime) qui portait une
        // SECONDE formule monétaire, divergente et jamais exécutée. Le calcul monétaire d'une vente vit désormais
        // dans SalePricingPolicy (Domain), unique propriétaire, réellement appelé par RegisterSaleUseCase.
        services.AddScoped<IAuthenticationService, AuthenticationService>();

        return services;
    }

    private static string ResolveConnectionString(IConfiguration? configuration, string? provided)
    {
        if (!string.IsNullOrWhiteSpace(provided))
        {
            return provided;
        }

        // Chemin unique résolu par SqliteDatabasePathResolver (P2A-1A) :
        // variable d'environnement MMV_DATABASE_PATH, sinon configuration, sinon défaut LOCALAPPDATA.
        var fromConfig = configuration?.GetConnectionString(SqliteDatabasePathResolver.ConnectionStringName);
        var dbPath = SqliteDatabasePathResolver.ResolveDatabasePath(configuredConnectionString: fromConfig);
        SqliteDatabasePathResolver.EnsureDirectoryExists(dbPath);

        return SqliteDatabasePathResolver.GetConnectionString(dbPath);
    }
}
