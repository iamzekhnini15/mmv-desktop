using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MMV.Domain.Interfaces.Repositories;
using MMV.Domain.Services;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Repositories;

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

        return services;
    }

    private static string ResolveConnectionString(IConfiguration? configuration, string? provided)
    {
        if (!string.IsNullOrWhiteSpace(provided))
        {
            return provided;
        }

        var fromConfig = configuration?.GetConnectionString("OpticDatabase");
        if (!string.IsNullOrWhiteSpace(fromConfig))
        {
            return fromConfig!;
        }

        // Fallback local SQLite path: %LOCALAPPDATA%\ManageMyVision\mmv.db
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ManageMyVision",
            "mmv.db");

        var directory = Path.GetDirectoryName(dbPath);
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory!);
        }

        return $"Data Source={dbPath}";
    }
}
