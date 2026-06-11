using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MMV.Domain.Interfaces.Repositories;
using MMV.Domain.Services;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Repositories;
using MMV.Infrastructure.Services;

namespace MMV.Infrastructure;

/// <summary>
/// Méthodes d'enregistrement des services d'infrastructure dans l'IoC container.
/// </summary>
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

        // Services métier
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IPrescriptionService, PrescriptionService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<ISaleService, SaleService>();
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
