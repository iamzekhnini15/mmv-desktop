using Microsoft.EntityFrameworkCore;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Infrastructure.Data.Configurations;

namespace MMV.Infrastructure.Data;

/// <summary>
/// DbContext principal pour la gestion des données de l'application ManageMyVision.
/// Gère les 9 tables principales et configure les relations avec Fluent API.
/// </summary>
public class OpticDbContext : DbContext
{
    /// <summary>
    /// Initialise une nouvelle instance de OpticDbContext.
    /// </summary>
    public OpticDbContext(DbContextOptions<OpticDbContext> options) : base(options) { }

    // ========== STAFF ==========
    /// <summary>
    /// Utilisateurs (personnel) du système.
    /// </summary>
    public DbSet<User> Users { get; set; } = null!;

    // ========== CATALOGUE ==========
    /// <summary>
    /// Fournisseurs de produits.
    /// </summary>
    public DbSet<Supplier> Suppliers { get; set; } = null!;

    /// <summary>
    /// Catégories de produits.
    /// </summary>
    public DbSet<ProductCategory> ProductCategories { get; set; } = null!;

    /// <summary>
    /// Produits du catalogue.
    /// </summary>
    public DbSet<Product> Products { get; set; } = null!;

    /// <summary>
    /// Détails spécifiques des verres.
    /// </summary>
    public DbSet<GlassDetail> GlassDetails { get; set; } = null!;

    /// <summary>
    /// Détails spécifiques des lentilles.
    /// </summary>
    public DbSet<LensDetail> LensDetails { get; set; } = null!;

    /// <summary>
    /// Détails spécifiques des accessoires/montures.
    /// </summary>
    public DbSet<AccessoryDetail> AccessoryDetails { get; set; } = null!;

    /// <summary>
    /// Suppléments optionnels pour verres.
    /// </summary>
    public DbSet<Supplement> Supplements { get; set; } = null!;

    /// <summary>
    /// Association verres-suppléments.
    /// </summary>
    public DbSet<GlassSupplement> GlassSupplements { get; set; } = null!;

    /// <summary>
    /// Grille tarifaire des verres.
    /// </summary>
    public DbSet<GlassPricingTier> GlassPricingTiers { get; set; } = null!;

    // ========== CRM ==========
    /// <summary>
    /// Clients de l'opticien.
    /// </summary>
    public DbSet<Customer> Customers { get; set; } = null!;

    // ========== MÉDICAL ==========
    /// <summary>
    /// Ordonnances ophtalmologiques.
    /// </summary>
    public DbSet<Prescription> Prescriptions { get; set; } = null!;

    // ========== WORKFLOW ATELIER ==========
    /// <summary>
    /// Commandes (workflow atelier).
    /// </summary>
    public DbSet<Order> Orders { get; set; } = null!;

    /// <summary>
    /// Articles des commandes.
    /// </summary>
    public DbSet<OrderItem> OrderItems { get; set; } = null!;

    // ========== POINT DE VENTE ==========
    /// <summary>
    /// Ventes (caisse/point de vente).
    /// </summary>
    public DbSet<Sale> Sales { get; set; } = null!;

    /// <summary>
    /// Articles des ventes.
    /// </summary>
    public DbSet<SaleItem> SaleItems { get; set; } = null!;

    // ========== INVENTAIRE ==========
    /// <summary>
    /// Mouvements de stock.
    /// </summary>
    public DbSet<StockMovement> StockMovements { get; set; } = null!;

    // ========== NOTIFICATIONS ==========
    /// <summary>
    /// Notifications système.
    /// </summary>
    public DbSet<Notification> Notifications { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            // Chemin unique résolu par SqliteDatabasePathResolver (P2A-1A).
            var dbPath = SqliteDatabasePathResolver.ResolveDatabasePath();
            SqliteDatabasePathResolver.EnsureDirectoryExists(dbPath);
            optionsBuilder.UseSqlite(SqliteDatabasePathResolver.GetConnectionString(dbPath));
        }

        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Appliquer les configurations Fluent API
        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new SupplierConfiguration());
        modelBuilder.ApplyConfiguration(new ProductCategoryConfiguration());
        modelBuilder.ApplyConfiguration(new ProductConfiguration());
        modelBuilder.ApplyConfiguration(new GlassDetailConfiguration());
        modelBuilder.ApplyConfiguration(new LensDetailConfiguration());
        modelBuilder.ApplyConfiguration(new AccessoryDetailConfiguration());
        modelBuilder.ApplyConfiguration(new SupplementConfiguration());
        modelBuilder.ApplyConfiguration(new GlassSupplementConfiguration());
        modelBuilder.ApplyConfiguration(new GlassPricingTierConfiguration());
        modelBuilder.ApplyConfiguration(new CustomerConfiguration());
        modelBuilder.ApplyConfiguration(new PrescriptionConfiguration());
        modelBuilder.ApplyConfiguration(new OrderConfiguration());
        modelBuilder.ApplyConfiguration(new OrderItemConfiguration());
        modelBuilder.ApplyConfiguration(new SaleConfiguration());
        modelBuilder.ApplyConfiguration(new SaleItemConfiguration());
        modelBuilder.ApplyConfiguration(new StockMovementConfiguration());
        modelBuilder.ApplyConfiguration(new NotificationConfiguration());
        // Les données initiales (admin, etc.) sont gérées dans DbInitializer.cs
    }

}
