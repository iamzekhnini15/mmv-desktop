using Microsoft.EntityFrameworkCore;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Infrastructure.Configuration;
using MMV.Infrastructure.Data.Configurations;
using MMV.Infrastructure.Data.Portability;

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

    /// <summary>
    /// Fiches atelier de montage (bons de travaux technicien), versionnées et immuables. P3-6B.
    /// </summary>
    public DbSet<WorkshopSheet> WorkshopSheets { get; set; } = null!;

    /// <summary>
    /// Lignes snapshot des fiches atelier. P3-6B.
    /// </summary>
    public DbSet<WorkshopSheetItem> WorkshopSheetItems { get; set; } = null!;

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

    // ========== NUMÉROTATION ==========
    /// <summary>
    /// Compteurs de séquences de numérotation des documents (ventes, commandes…). P2A-1E, R-03 / ADR-006.
    /// </summary>
    public DbSet<DocumentSequence> DocumentSequences { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Garde inchangée : lorsque les options viennent du conteneur DI ou d'un test (UseSqlite(connection)),
        // rien n'est reconfiguré ici — le fournisseur fourni par l'appelant fait autorité.
        if (!optionsBuilder.IsConfigured)
        {
            // P4-3 : fournisseur sélectionné par configuration (MMV_DATABASE_PROVIDER), défaut SQLite.
            var providerOptions = DatabaseProviderResolver.Resolve();

            string? dbPath = null;
            if (providerOptions.Provider == DatabaseProvider.Sqlite)
            {
                // Chemin unique résolu par SqliteDatabasePathResolver (P2A-1A).
                dbPath = SqliteDatabasePathResolver.ResolveDatabasePath();
                SqliteDatabasePathResolver.EnsureDirectoryExists(dbPath);
            }

            DatabaseProviderResolver.Configure(optionsBuilder, providerOptions, dbPath);
        }

        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // P4-5C : POINT DE LECTURE UNIQUE du provider pour la construction du modèle (ADR-PROD-DB-006 X2).
        // Deux constructions du modèle ne s'écrivent pas de la même façon sur SQLite et sur PostgreSQL —
        // le type physique des colonnes monétaires et le filtre de l'index unique partiel des fiches
        // atelier. Le nom du provider est lu ICI, une seule fois, puis PASSÉ aux configurations qui en ont
        // besoin : aucune IEntityTypeConfiguration n'interroge le provider pour son propre compte, et un
        // provider inconnu fait lever ModelPortability.For au lieu de deviner une forme.
        var portability = ModelPortability.For(Database.ProviderName);

        // Appliquer les configurations Fluent API
        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new SupplierConfiguration());
        modelBuilder.ApplyConfiguration(new ProductCategoryConfiguration());
        modelBuilder.ApplyConfiguration(new ProductConfiguration(portability));
        modelBuilder.ApplyConfiguration(new GlassDetailConfiguration());
        modelBuilder.ApplyConfiguration(new LensDetailConfiguration());
        modelBuilder.ApplyConfiguration(new AccessoryDetailConfiguration());
        modelBuilder.ApplyConfiguration(new SupplementConfiguration(portability));
        modelBuilder.ApplyConfiguration(new GlassSupplementConfiguration());
        modelBuilder.ApplyConfiguration(new GlassPricingTierConfiguration(portability));
        modelBuilder.ApplyConfiguration(new CustomerConfiguration());
        modelBuilder.ApplyConfiguration(new PrescriptionConfiguration());
        modelBuilder.ApplyConfiguration(new OrderConfiguration());
        modelBuilder.ApplyConfiguration(new OrderItemConfiguration(portability));
        modelBuilder.ApplyConfiguration(new WorkshopSheetConfiguration(portability));
        modelBuilder.ApplyConfiguration(new WorkshopSheetItemConfiguration());
        modelBuilder.ApplyConfiguration(new SaleConfiguration(portability));
        modelBuilder.ApplyConfiguration(new SaleItemConfiguration(portability));
        modelBuilder.ApplyConfiguration(new StockMovementConfiguration());
        modelBuilder.ApplyConfiguration(new NotificationConfiguration());
        modelBuilder.ApplyConfiguration(new DocumentSequenceConfiguration());
        // Les données initiales (admin, etc.) sont gérées dans DbInitializer.cs
    }

}
