using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Infrastructure.Data;
using Xunit;

namespace MMV.Domain.Tests.Data;

/// <summary>
/// P2A-1R19 — correction du bruit EF Core « pending model changes » provoqué par les défauts figés
/// <c>HasDefaultValue(DateTime.UtcNow)</c> (anti-pattern R-19). Le correctif retenu (Option B) supprime
/// ces défauts SQL figés : l'horodatage reste géré par l'application (initialiseurs d'entité, futur
/// <c>IClock</c>). Une migration technique <c>FixDateTimeDefaultValues</c> aligne le schéma et le snapshot.
///
/// Ces tests prouvent : (1) le modèle n'a plus de dérive vis-à-vis du dernier snapshot de migration ;
/// (2) une base vierge se crée via migrations sans donnée de démonstration ; (3) les colonnes de date
/// concernées acceptent une insertion et conservent la valeur applicative ; (4) la migration de
/// correction n'est pas destructive (les lignes existantes sont conservées). Tous les tests utilisent
/// des fichiers temporaires isolés (jamais la base utilisateur réelle).
/// </summary>
public sealed class DateTimeDefaultValuesMigrationTests : IDisposable
{
    /// <summary>Dernière migration AVANT le correctif R-19 (état de référence pour la non-destruction).</summary>
    private const string MigrationBeforeFix = "20260212220902_RestoreSaleOrderSeparation";

    /// <summary>Migration technique de correction R-19 créée par cette phase.</summary>
    private const string FixMigration = "20260611114307_FixDateTimeDefaultValues";

    private readonly string _workDirectory;

    public DateTimeDefaultValuesMigrationTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-r19-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDirectory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_workDirectory))
            {
                Directory.Delete(_workDirectory, recursive: true);
            }
        }
        catch
        {
            // Nettoyage best-effort.
        }
    }

    private string PathFor(string fileName) => Path.Combine(_workDirectory, fileName);

    private static OpticDbContext CreateContext(string databasePath)
    {
        var connectionString = $"Data Source={databasePath};Pooling=False";
        var options = new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite(connectionString)
            .Options;
        return new OpticDbContext(options);
    }

    // ---------------------------------------------------------------------
    // (1) Plus aucune dérive modèle ↔ snapshot (équivalent du CLI has-pending-model-changes)
    // ---------------------------------------------------------------------

    [Fact]
    public void Model_HasNoPendingModelChanges_AfterFixMigration()
    {
        var dbPath = PathFor("model-check.db");
        using var context = CreateContext(dbPath);

        context.Database.HasPendingModelChanges().Should().BeFalse(
            "le correctif R-19 supprime les défauts DateTime figés : le modèle correspond au dernier " +
            "snapshot de migration (équivalent de `dotnet ef migrations has-pending-model-changes` = false)");
    }

    // ---------------------------------------------------------------------
    // (2) Base vierge créée via migrations, sans donnée de démonstration
    // ---------------------------------------------------------------------

    [Fact]
    public void Migrate_FreshDatabase_AppliesFixMigration_NoPending_NoDemoData()
    {
        var dbPath = PathFor("fresh-fix.db");
        using (var context = CreateContext(dbPath))
        {
            context.Database.Migrate();
        }
        SqliteConnection.ClearAllPools();

        using var verify = CreateContext(dbPath);
        verify.Database.GetAppliedMigrations().Should().Contain(FixMigration);
        verify.Database.GetAppliedMigrations().Should().BeEquivalentTo(verify.Database.GetMigrations());
        verify.Database.GetPendingMigrations().Should().BeEmpty();

        // La migration de correction n'introduit aucune donnée de démonstration.
        verify.Users.Count().Should().Be(0);
        verify.Customers.Count().Should().Be(0);
        verify.Products.Count().Should().Be(0);
    }

    // ---------------------------------------------------------------------
    // (3) Les 7 colonnes de date concernées acceptent une insertion et conservent
    //     la valeur fournie par l'application (initialiseur d'entité), sans défaut SQL.
    // ---------------------------------------------------------------------

    [Fact]
    public void MigratedDatabase_AffectedDateColumns_AcceptInsertion_AndPersistApplicationTimestamps()
    {
        var dbPath = PathFor("insertions.db");
        var lowerBound = DateTime.UtcNow.AddMinutes(-5);

        using (var context = CreateContext(dbPath))
        {
            context.Database.Migrate();

            // Entités sans clé étrangère obligatoire.
            var user = new User
            {
                Username = "user-r19",
                PasswordHash = "hash",
                FirstName = "Iris",
                LastName = "Test",
                Role = UserRole.Admin
            };
            var customer = new Customer { FirstName = "Client", LastName = "R19" };
            var supplier = new Supplier { Name = "Fournisseur R19" };
            context.Users.Add(user);
            context.Customers.Add(customer);
            context.Suppliers.Add(supplier);
            context.SaveChanges();

            // Entités dépendant des précédentes.
            var product = new Product
            {
                Reference = "REF-R19",
                Name = "Produit R19",
                SupplierId = supplier.SupplierId,
                Category = ProductCategoryEnum.MONTURE
            };
            var sale = new Sale
            {
                SaleNumber = "VTE-R19",
                PaymentMethod = PaymentMethod.Cash,
                TotalAmount = 100m,
                FinalAmount = 100m
            };
            var prescription = new Prescription
            {
                CustomerId = customer.CustomerId,
                IssueDate = DateTime.UtcNow.Date
            };
            context.Products.Add(product);
            context.Sales.Add(sale);
            context.Prescriptions.Add(prescription);
            context.SaveChanges();

            var order = new Order { OrderNumber = "CMD-R19", SaleId = sale.SaleId };
            var movement = new StockMovement
            {
                ProductId = product.ProductId,
                MovementType = StockMovementType.In,
                Quantity = 5
            };
            context.Orders.Add(order);
            context.StockMovements.Add(movement);
            context.SaveChanges();
        }
        SqliteConnection.ClearAllPools();

        using var verify = CreateContext(dbPath);

        // Chaque colonne de date concernée par R-19 a bien reçu la valeur applicative (≈ maintenant),
        // alors qu'aucun défaut SQL n'existe plus en base.
        verify.Users.Single().CreatedAt.Should().BeAfter(lowerBound);

        var persistedCustomer = verify.Customers.Single();
        persistedCustomer.CreatedAt.Should().BeAfter(lowerBound);
        persistedCustomer.UpdatedAt.Should().BeAfter(lowerBound);

        verify.Sales.Single().SaleDate.Should().BeAfter(lowerBound);
        verify.Orders.Single().OrderDate.Should().BeAfter(lowerBound);
        verify.Prescriptions.Single().CreatedAt.Should().BeAfter(lowerBound);
        verify.StockMovements.Single().CreatedAt.Should().BeAfter(lowerBound);
    }

    // ---------------------------------------------------------------------
    // (4) La migration de correction n'est pas destructive : les lignes préexistantes
    //     (créées avant le correctif) sont conservées après application (reconstruction SQLite).
    // ---------------------------------------------------------------------

    [Fact]
    public void FixMigration_IsNonDestructive_PreservesExistingRows()
    {
        var dbPath = PathFor("nondestructive.db");

        DateTime customerCreatedAtBefore;
        using (var context = CreateContext(dbPath))
        {
            // Amène la base à l'état immédiatement AVANT le correctif R-19.
            context.GetService<IMigrator>().Migrate(MigrationBeforeFix);
            context.Database.GetAppliedMigrations().Should().NotContain(FixMigration);

            var customer = new Customer { FirstName = "Avant", LastName = "Correctif" };
            context.Customers.Add(customer);
            context.SaveChanges();
            customerCreatedAtBefore = customer.CreatedAt;
        }
        SqliteConnection.ClearAllPools();

        // Applique la migration de correction (reconstruction de table SQLite des colonnes de date).
        using (var context = CreateContext(dbPath))
        {
            context.Database.Migrate();
        }
        SqliteConnection.ClearAllPools();

        using var verify = CreateContext(dbPath);
        verify.Database.GetAppliedMigrations().Should().Contain(FixMigration);
        verify.Database.GetPendingMigrations().Should().BeEmpty();

        var preserved = verify.Customers.Should().ContainSingle().Subject;
        preserved.FirstName.Should().Be("Avant");
        preserved.LastName.Should().Be("Correctif");
        preserved.CreatedAt.Should().BeCloseTo(customerCreatedAtBefore, TimeSpan.FromSeconds(1),
            "la reconstruction de table par la migration conserve la valeur d'horodatage existante");
    }
}
