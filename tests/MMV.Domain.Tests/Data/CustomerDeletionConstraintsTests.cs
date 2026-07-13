using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Infrastructure.Data;
using Xunit;

namespace MMV.Domain.Tests.Data;

/// <summary>
/// P3-2B — Contraintes <b>réelles</b> de la base autour de la suppression d'un client, sur un <b>vrai SQLite</b>
/// (jamais le provider InMemory, qui n'applique aucune clé étrangère).
///
/// <para>
/// Ces tests contournent délibérément le garde applicatif (<c>DeleteCustomerUseCase</c>) : ils suppriment le
/// client <b>directement</b> via le <see cref="OpticDbContext"/>. Ils prouvent donc que la base est le
/// <b>dernier rempart</b> — celui qui tient même si le contrôle applicatif est contourné, ou s'il perd la course
/// « check-then-delete » face à un autre poste qui crée un historique entre la vérification et le <c>DELETE</c>
/// (ADR-PROD-DB-001).
/// </para>
///
/// <para>
/// Avant P3-2B, ces mêmes scénarios <b>réussissaient</b> silencieusement : l'ordonnance était détruite en cascade
/// (<c>Cascade</c>) et la vente était anonymisée (<c>SetNull</c>). Les deux clés étrangères sont désormais en
/// <c>Restrict</c>.
/// </para>
/// </summary>
public sealed class CustomerDeletionConstraintsTests : IDisposable
{
    private readonly string _workDirectory;

    public CustomerDeletionConstraintsTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p32b-fk-" + Guid.NewGuid().ToString("N"));
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

    private static void EnsureSchema(string databasePath)
    {
        using var context = CreateContext(databasePath);
        context.Database.EnsureCreated();
    }

    /// <summary>
    /// Vérifie que SQLite applique bien les clés étrangères sur cette connexion. Sans ce PRAGMA à <c>1</c>, les
    /// assertions ci-dessous ne prouveraient rien (SQLite ignorerait silencieusement les FK).
    /// </summary>
    private static bool ForeignKeysEnforced(OpticDbContext context)
    {
        var connection = context.Database.GetDbConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys";
        var value = command.ExecuteScalar();
        return Convert.ToInt64(value) == 1;
    }

    private static long SeedCustomer(string databasePath)
    {
        using var context = CreateContext(databasePath);
        var customer = new Customer { FirstName = "Jean", LastName = "Dupont" };
        context.Customers.Add(customer);
        context.SaveChanges();
        return customer.CustomerId;
    }

    private static Prescription NewPrescription(long customerId) => new()
    {
        CustomerId = customerId,
        IssueDate = DateTime.UtcNow.AddDays(-5),
        DoctorName = "Dr Martin"
    };

    private static Sale NewSale(long customerId) => new()
    {
        SaleNumber = "VTE-FK-0001",
        CustomerId = customerId,
        SaleDate = DateTime.UtcNow.AddDays(-3),
        TotalAmount = 100m,
        FinalAmount = 100m,
        PaymentMethod = PaymentMethod.Cash,
        Status = SaleStatus.Delivered
    };

    // ------------------------------------------------------------------
    // (0) Préalable : les clés étrangères SONT appliquées par SQLite
    // ------------------------------------------------------------------

    [Fact]
    public void Sqlite_EnforcesForeignKeys()
    {
        var dbPath = PathFor("pragma.db");
        EnsureSchema(dbPath);

        using var context = CreateContext(dbPath);

        ForeignKeysEnforced(context).Should().BeTrue(
            "sans PRAGMA foreign_keys=ON, les contraintes Restrict ne protégeraient rien");
    }

    // ------------------------------------------------------------------
    // (1) Client AVEC ordonnance : le DELETE direct échoue, tout est conservé
    // ------------------------------------------------------------------

    [Fact]
    public void DeletingCustomerWithPrescription_IsRejectedByDatabase_AndKeepsBothRows()
    {
        var dbPath = PathFor("fk-prescription.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);

        using (var seed = CreateContext(dbPath))
        {
            seed.Prescriptions.Add(NewPrescription(customerId));
            seed.SaveChanges();
        }

        using (var context = CreateContext(dbPath))
        {
            // Suppression DIRECTE : le garde applicatif est délibérément court-circuité.
            var customer = context.Customers.Single(c => c.CustomerId == customerId);
            context.Customers.Remove(customer);

            Action act = () => context.SaveChanges();

            act.Should().Throw<DbUpdateException>(
                "la FK Prescriptions → Customers est en Restrict : la base refuse la suppression");
        }

        using var verify = CreateContext(dbPath);
        verify.Customers.AsNoTracking().Should().ContainSingle(c => c.CustomerId == customerId,
            "le client est conservé");
        verify.Prescriptions.AsNoTracking().Should().ContainSingle(p => p.CustomerId == customerId,
            "l'ordonnance est conservée (plus aucune cascade destructrice)");
    }

    // ------------------------------------------------------------------
    // (2) Client AVEC vente : le DELETE direct échoue, tout est conservé (pas de SetNull)
    // ------------------------------------------------------------------

    [Fact]
    public void DeletingCustomerWithSale_IsRejectedByDatabase_AndKeepsBothRows()
    {
        var dbPath = PathFor("fk-sale.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);

        using (var seed = CreateContext(dbPath))
        {
            seed.Sales.Add(NewSale(customerId));
            seed.SaveChanges();
        }

        using (var context = CreateContext(dbPath))
        {
            var customer = context.Customers.Single(c => c.CustomerId == customerId);
            context.Customers.Remove(customer);

            Action act = () => context.SaveChanges();

            act.Should().Throw<DbUpdateException>(
                "la FK Sales → Customers est en Restrict : la base refuse la suppression");
        }

        using var verify = CreateContext(dbPath);
        verify.Customers.AsNoTracking().Should().ContainSingle(c => c.CustomerId == customerId,
            "le client est conservé");

        var sale = verify.Sales.AsNoTracking().Single();
        sale.CustomerId.Should().Be(customerId,
            "la vente reste rattachée à son client : le SetNull d'origine (anonymisation) est supprimé");
    }

    // ------------------------------------------------------------------
    // (3) Client SANS historique : le DELETE direct réussit
    // ------------------------------------------------------------------

    [Fact]
    public void DeletingCustomerWithoutHistory_Succeeds()
    {
        var dbPath = PathFor("fk-clean.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);

        using (var context = CreateContext(dbPath))
        {
            var customer = context.Customers.Single(c => c.CustomerId == customerId);
            context.Customers.Remove(customer);

            context.SaveChanges();
        }

        using var verify = CreateContext(dbPath);
        verify.Customers.AsNoTracking().Should().BeEmpty(
            "un client vierge (aucune ordonnance, aucune vente) reste physiquement supprimable");
    }

    // ------------------------------------------------------------------
    // (4) Course multi-poste : l'historique créé APRÈS la vérification bloque quand même le DELETE
    // ------------------------------------------------------------------

    [Fact]
    public void HistoryCreatedAfterTheCheck_StillBlocksTheDelete()
    {
        var dbPath = PathFor("fk-race.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);

        // Poste A : charge le client et constate (à cet instant) qu'il n'a aucun historique.
        using var postA = CreateContext(dbPath);
        var customer = postA.Customers.Single(c => c.CustomerId == customerId);
        postA.Prescriptions.Any(p => p.CustomerId == customerId).Should().BeFalse();
        postA.Sales.Any(s => s.CustomerId == customerId).Should().BeFalse();

        // Poste B : crée une vente pour ce client entre la vérification de A et son DELETE.
        using (var postB = CreateContext(dbPath))
        {
            postB.Sales.Add(NewSale(customerId));
            postB.SaveChanges();
        }

        // Poste A : supprime sur la foi de sa lecture obsolète → la base refuse.
        postA.Customers.Remove(customer);
        Action act = () => postA.SaveChanges();

        act.Should().Throw<DbUpdateException>(
            "la contrainte FK est le rempart atomique : le contrôle applicatif seul perdrait la course");

        using var verify = CreateContext(dbPath);
        verify.Customers.AsNoTracking().Should().ContainSingle(c => c.CustomerId == customerId);
        verify.Sales.AsNoTracking().Should().ContainSingle(s => s.CustomerId == customerId);
    }
}
