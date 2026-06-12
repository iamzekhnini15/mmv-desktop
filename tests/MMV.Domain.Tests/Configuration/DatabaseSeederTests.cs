using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Interfaces.Persistence;
using MMV.Infrastructure.Configuration;
using MMV.Infrastructure.Data;
using Xunit;

namespace MMV.Domain.Tests.Configuration;

/// <summary>
/// P2A-1F — Comportement du <see cref="DatabaseSeeder"/> sur une <b>vraie base SQLite migrée</b>.
///
/// Réalité du schéma : la migration <c>InitialCreate</c> insère un compte <c>admin</c> par défaut, mais la
/// migration <c>AddCounterSaleFieldsToOrder</c> le supprime (<c>DeleteData UserId=1</c>). Une base migrée
/// jusqu'à la tête ne contient donc <b>aucun</b> utilisateur. Un compte faible <c>admin/admin</c> ne peut
/// provenir que du jeu de démonstration ou d'une base historique : ces tests le simulent explicitement pour
/// prouver sa neutralisation, et vérifient que la production ne crée aucune donnée de démonstration ni compte
/// faible, que le mode démonstration explicite seede sans collision, et que le cycle SQLite + la numérotation
/// restent opérationnels.
/// </summary>
public sealed class DatabaseSeederTests : IDisposable
{
    private const string WeakAdminHash = "$2a$11$dXJ3SW6G7P50eS6xFJwFHeJ/hbtjiZlyCloO/sURR8EZ4/nqXJcOy";

    private readonly string _workDirectory;

    public DatabaseSeederTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p2a1f-seed-" + Guid.NewGuid().ToString("N"));
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

    private static OpticDbContext CreateContext(string databasePath)
    {
        var options = new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False")
            .Options;
        return new OpticDbContext(options);
    }

    /// <summary>Crée une base migrée jusqu'à la tête (aucun utilisateur : l'admin de migration a été supprimé).</summary>
    private string CreateMigratedDatabase()
    {
        var dbPath = Path.Combine(_workDirectory, "seed-" + Guid.NewGuid().ToString("N") + ".db");
        using (var context = CreateContext(dbPath))
        {
            context.Database.Migrate();
        }
        SqliteConnection.ClearAllPools();
        return dbPath;
    }

    /// <summary>Simule une base portant un compte <c>admin/admin</c> faible (jeu de démonstration ou base historique).</summary>
    private void SeedWeakAdmin(string dbPath)
    {
        using (var context = CreateContext(dbPath))
        {
            context.Users.Add(new User
            {
                Username = "admin",
                PasswordHash = WeakAdminHash,
                FirstName = "Administrateur",
                LastName = "Système",
                Role = UserRole.Admin,
                IsActive = true
            });
            context.SaveChanges();
        }
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public void Production_FreshInstall_CreatesNoDemoData_AndNoAdmin()
    {
        var dbPath = CreateMigratedDatabase();
        var options = new SeedOptions { Environment = ApplicationEnvironment.Production };

        SeedResult result;
        using (var context = CreateContext(dbPath))
        {
            result = new DatabaseSeeder().Seed(context, options);
        }
        SqliteConnection.ClearAllPools();

        result.DemoSeedApplied.Should().BeFalse();
        result.BootstrapAdminConfigured.Should().BeFalse();

        using var verify = CreateContext(dbPath);
        verify.Customers.Should().BeEmpty("la production ne crée aucune donnée de démonstration");
        verify.Products.Should().BeEmpty();
        verify.Users.Should().BeEmpty("aucun compte admin/admin n'est créé automatiquement");
    }

    [Fact]
    public void Production_WithExistingWeakAdmin_NeutralizesIt()
    {
        var dbPath = CreateMigratedDatabase();
        SeedWeakAdmin(dbPath);
        var options = new SeedOptions { Environment = ApplicationEnvironment.Production };

        SeedResult result;
        using (var context = CreateContext(dbPath))
        {
            result = new DatabaseSeeder().Seed(context, options);
        }
        SqliteConnection.ClearAllPools();

        result.WeakDefaultAdminNeutralized.Should().BeTrue();

        using var verify = CreateContext(dbPath);
        var admin = verify.Users.Single(u => u.Username == "admin");
        admin.IsActive.Should().BeFalse("le compte admin/admin faible est désactivé : aucune connexion possible");
        admin.PasswordHash.Should().Be(WeakAdminHash, "le mot de passe n'est pas changé, seul l'accès est coupé");
    }

    [Fact]
    public void Production_WithBootstrapSecret_FreshInstall_CreatesStrongAdmin()
    {
        var dbPath = CreateMigratedDatabase();
        var options = new SeedOptions
        {
            Environment = ApplicationEnvironment.Production,
            BootstrapAdminUsername = "gerant",
            BootstrapAdminPassword = "Str0ngBootstrap"
        };

        SeedResult result;
        using (var context = CreateContext(dbPath))
        {
            result = new DatabaseSeeder().Seed(context, options);
        }
        SqliteConnection.ClearAllPools();

        result.BootstrapAdminConfigured.Should().BeTrue();

        using var verify = CreateContext(dbPath);
        verify.Customers.Should().BeEmpty();
        var admin = verify.Users.Single();
        admin.Username.Should().Be("gerant");
        admin.Role.Should().Be(UserRole.Admin);
        admin.IsActive.Should().BeTrue();
        BCrypt.Net.BCrypt.Verify("Str0ngBootstrap", admin.PasswordHash).Should().BeTrue();
    }

    [Fact]
    public void Production_WithBootstrapSecret_ExistingWeakAdmin_SecuresIt()
    {
        var dbPath = CreateMigratedDatabase();
        SeedWeakAdmin(dbPath);
        var options = new SeedOptions
        {
            Environment = ApplicationEnvironment.Production,
            BootstrapAdminUsername = "admin",
            BootstrapAdminPassword = "Str0ngBootstrap"
        };

        SeedResult result;
        using (var context = CreateContext(dbPath))
        {
            result = new DatabaseSeeder().Seed(context, options);
        }
        SqliteConnection.ClearAllPools();

        result.BootstrapAdminConfigured.Should().BeTrue();
        result.WeakDefaultAdminNeutralized.Should().BeFalse();

        using var verify = CreateContext(dbPath);
        var admin = verify.Users.Single(u => u.Username == "admin");
        admin.IsActive.Should().BeTrue();
        admin.PasswordHash.Should().NotBe(WeakAdminHash);
        BCrypt.Net.BCrypt.Verify("Str0ngBootstrap", admin.PasswordHash).Should().BeTrue();
        BCrypt.Net.BCrypt.Verify("admin", admin.PasswordHash).Should().BeFalse("le mot de passe faible n'est plus valide");
    }

    [Fact]
    public void Development_WithExplicitDemoSeed_SeedsDemoData()
    {
        var dbPath = CreateMigratedDatabase();
        var options = new SeedOptions
        {
            Environment = ApplicationEnvironment.Development,
            EnableDemoSeed = true
        };

        SeedResult result;
        using (var context = CreateContext(dbPath))
        {
            result = new DatabaseSeeder().Seed(context, options);
        }
        SqliteConnection.ClearAllPools();

        result.DemoSeedApplied.Should().BeTrue();

        using var verify = CreateContext(dbPath);
        verify.Customers.Should().HaveCount(50, "le jeu de démonstration est appliqué");
        verify.Products.Should().NotBeEmpty();
        verify.Users.Select(u => u.Username).Should().Contain("marie.optic");
    }

    [Fact]
    public void Demo_WithPreExistingAdmin_DoesNotCollideOnUsername()
    {
        // Idempotence du seed utilisateurs : un « admin » déjà présent (base historique) + jeu de
        // démonstration ⇒ pas de violation de l'index unique Username, un seul « admin » subsiste.
        var dbPath = CreateMigratedDatabase();
        SeedWeakAdmin(dbPath);
        var options = new SeedOptions
        {
            Environment = ApplicationEnvironment.Demonstration,
            EnableDemoSeed = true
        };

        using (var context = CreateContext(dbPath))
        {
            new DatabaseSeeder().Seed(context, options);
        }
        SqliteConnection.ClearAllPools();

        using var verify = CreateContext(dbPath);
        verify.Customers.Should().HaveCount(50);
        verify.Users.Count(u => u.Username == "admin").Should().Be(1, "aucune collision : un seul admin");
        verify.Users.Select(u => u.Username).Should().Contain("marie.optic");
    }

    [Fact]
    public void Production_EvenIfDemoRequested_DoesNotSeedDemoData()
    {
        // Défense en profondeur : EnableDemoSeed == true mais environnement Production ⇒ aucun seed démo.
        var dbPath = CreateMigratedDatabase();
        var options = new SeedOptions
        {
            Environment = ApplicationEnvironment.Production,
            EnableDemoSeed = true
        };

        using (var context = CreateContext(dbPath))
        {
            new DatabaseSeeder().Seed(context, options);
        }
        SqliteConnection.ClearAllPools();

        using var verify = CreateContext(dbPath);
        verify.Customers.Should().BeEmpty();
    }

    [Fact]
    public void Test_Environment_DoesNotSeedDemoData_AndNeutralizesWeakAdmin()
    {
        var dbPath = CreateMigratedDatabase();
        SeedWeakAdmin(dbPath);
        var options = new SeedOptions { Environment = ApplicationEnvironment.Test, EnableDemoSeed = true };

        using (var context = CreateContext(dbPath))
        {
            new DatabaseSeeder().Seed(context, options);
        }
        SqliteConnection.ClearAllPools();

        using var verify = CreateContext(dbPath);
        verify.Customers.Should().BeEmpty();
        verify.Users.Single(u => u.Username == "admin").IsActive.Should().BeFalse();
    }

    [Fact]
    public void Seeder_IsIdempotent_OnRepeatedProductionRuns()
    {
        var dbPath = CreateMigratedDatabase();
        SeedWeakAdmin(dbPath);
        var options = new SeedOptions { Environment = ApplicationEnvironment.Production };

        using (var context = CreateContext(dbPath))
        {
            new DatabaseSeeder().Seed(context, options);
        }
        SqliteConnection.ClearAllPools();

        // Deuxième passage : le compte faible est déjà désactivé, rien de neuf n'est neutralisé ni créé.
        SeedResult second;
        using (var context = CreateContext(dbPath))
        {
            second = new DatabaseSeeder().Seed(context, options);
        }
        SqliteConnection.ClearAllPools();

        second.WeakDefaultAdminNeutralized.Should().BeFalse();
        using var verify = CreateContext(dbPath);
        verify.Users.Should().ContainSingle(u => u.Username == "admin");
        verify.Users.Single(u => u.Username == "admin").IsActive.Should().BeFalse();
        verify.Customers.Should().BeEmpty();
    }

    [Fact]
    public void AfterMigrateAndSeed_SqliteLifecycle_AndDocumentSequences_RemainOperational()
    {
        var dbPath = CreateMigratedDatabase();
        var options = new SeedOptions { Environment = ApplicationEnvironment.Production };

        using (var context = CreateContext(dbPath))
        {
            new DatabaseSeeder().Seed(context, options);
        }
        SqliteConnection.ClearAllPools();

        using var verify = CreateContext(dbPath);
        verify.Database.GetPendingMigrations().Should().BeEmpty("le cycle SQLite reste cohérent");
        verify.Database.HasPendingModelChanges().Should().BeFalse();
        verify.DocumentSequences.Select(s => s.SequenceName)
            .Should().Contain(new[] { DocumentSequenceNames.Sale, DocumentSequenceNames.Order });
    }
}
