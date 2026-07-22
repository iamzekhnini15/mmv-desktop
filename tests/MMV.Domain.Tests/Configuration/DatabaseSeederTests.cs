using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Policies;
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
                NormalizedUsername = UserIdentityPolicy.NormalizeUsername("admin"),
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

    // ================================================================================================
    // P3-10 (R4) — Transition démonstration → production : TOUS les comptes faibles sont neutralisés.
    // ================================================================================================

    /// <summary>Hash BCrypt du jeu de démonstration (mot de passe « admin »), porté par les quatre comptes.</summary>
    private const string DemoWeakHash = "$2a$11$QA85M79Q7bLajCAGRrVS1ONdstKLbV0S/vX6OKtN3CsCF3MWSNMoi";

    /// <summary>
    /// Simule une base autrefois seedée en <b>démonstration</b> puis exploitée en production : quatre comptes
    /// actifs portant tous le hash faible connu publiquement. C'est le scénario que la version antérieure du
    /// seeder traitait mal — son <c>FirstOrDefault</c> n'en neutralisait qu'un seul (audit §24, R4).
    /// </summary>
    private void SeedDemoWeakAccounts(string dbPath)
    {
        using (var context = CreateContext(dbPath))
        {
            foreach (var (username, role) in new[]
                     {
                         ("admin", UserRole.Admin),
                         ("marie.optic", UserRole.Optician),
                         ("pierre.tech", UserRole.Technician),
                         ("sophie.optic", UserRole.Optician),
                     })
            {
                context.Users.Add(new User
                {
                    Username = username,
                    NormalizedUsername = UserIdentityPolicy.NormalizeUsername(username),
                    PasswordHash = DemoWeakHash,
                    FirstName = "Demo",
                    LastName = username,
                    Role = role,
                    IsActive = true,
                });
            }

            context.SaveChanges();
        }
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public void Production_DemoSeededDatabase_WithoutSecret_DeactivatesEveryWeakAccount()
    {
        var dbPath = CreateMigratedDatabase();
        SeedDemoWeakAccounts(dbPath);
        var options = new SeedOptions { Environment = ApplicationEnvironment.Production };

        SeedResult result;
        using (var context = CreateContext(dbPath))
        {
            result = new DatabaseSeeder().Seed(context, options);
        }
        SqliteConnection.ClearAllPools();

        result.WeakDefaultAdminNeutralized.Should().BeTrue();

        using var verify = CreateContext(dbPath);
        verify.Users.Should().HaveCount(4, "aucun compte n'est supprimé, seulement désactivé");
        verify.Users.Where(u => u.PasswordHash == DemoWeakHash).Should()
            .OnlyContain(u => !u.IsActive, "AUCUN identifiant faible connu ne reste actif");
    }

    [Fact]
    public void Production_DemoSeededDatabase_WithSecret_SecuresCanonicalAdmin_AndDeactivatesTheOthers()
    {
        var dbPath = CreateMigratedDatabase();
        SeedDemoWeakAccounts(dbPath);
        const string strongSecret = "Str0ngBootstrapSecret";
        var options = new SeedOptions
        {
            Environment = ApplicationEnvironment.Production,
            BootstrapAdminPassword = strongSecret,
        };

        SeedResult result;
        using (var context = CreateContext(dbPath))
        {
            result = new DatabaseSeeder().Seed(context, options);
        }
        SqliteConnection.ClearAllPools();

        result.BootstrapAdminConfigured.Should().BeTrue();

        using var verify = CreateContext(dbPath);

        var admin = verify.Users.Single(u => u.NormalizedUsername == options.BootstrapAdminUsername.ToLowerInvariant());
        admin.IsActive.Should().BeTrue("le compte administrateur canonique reste utilisable");
        admin.PasswordHash.Should().NotBe(DemoWeakHash);
        admin.PasswordHash.Should().StartWith("$2a$11$", "rehaché avec BCrypt WF11");
        BCrypt.Net.BCrypt.Verify(strongSecret, admin.PasswordHash).Should().BeTrue();

        verify.Users.Where(u => u.PasswordHash == DemoWeakHash).Should()
            .HaveCount(3).And.OnlyContain(u => !u.IsActive,
                "fournir un secret ne doit laisser subsister aucun AUTRE identifiant faible actif");

        // Le secret ne doit jamais transiter par le résultat de seed ni ses notes.
        result.Notes.Should().NotContain(strongSecret);
    }

    [Fact]
    public void Production_DemoSeededDatabase_SecondRun_IsIdempotent()
    {
        var dbPath = CreateMigratedDatabase();
        SeedDemoWeakAccounts(dbPath);
        var options = new SeedOptions { Environment = ApplicationEnvironment.Production };

        using (var context = CreateContext(dbPath))
        {
            new DatabaseSeeder().Seed(context, options);
        }
        SqliteConnection.ClearAllPools();

        SeedResult second;
        using (var context = CreateContext(dbPath))
        {
            second = new DatabaseSeeder().Seed(context, options);
        }
        SqliteConnection.ClearAllPools();

        second.WeakDefaultAdminNeutralized.Should().BeFalse("tout est déjà neutralisé : aucun changement");

        using var verify = CreateContext(dbPath);
        verify.Users.Should().HaveCount(4);
        verify.Users.Should().OnlyContain(u => !u.IsActive);
    }

    /// <summary>
    /// Un compte réel (mot de passe déjà changé par l'exploitant) ne doit jamais être désactivé par le seed :
    /// la neutralisation vise les hash faibles CONNUS, pas les comptes en général.
    /// </summary>
    [Fact]
    public void Production_RealAccount_IsNeverDeactivated()
    {
        var dbPath = CreateMigratedDatabase();
        SeedDemoWeakAccounts(dbPath);

        var realHash = BCrypt.Net.BCrypt.HashPassword("R3alStrongPass", 11);
        using (var context = CreateContext(dbPath))
        {
            context.Users.Add(new User
            {
                Username = "patron",
                NormalizedUsername = UserIdentityPolicy.NormalizeUsername("patron"),
                PasswordHash = realHash,
                FirstName = "Vrai", LastName = "Compte", Role = UserRole.Admin, IsActive = true,
            });
            context.SaveChanges();
        }
        SqliteConnection.ClearAllPools();

        using (var context = CreateContext(dbPath))
        {
            new DatabaseSeeder().Seed(context, new SeedOptions { Environment = ApplicationEnvironment.Production });
        }
        SqliteConnection.ClearAllPools();

        using var verify = CreateContext(dbPath);
        var real = verify.Users.Single(u => u.NormalizedUsername == "patron");
        real.IsActive.Should().BeTrue("un compte non faible n'est jamais désactivé");
        real.PasswordHash.Should().Be(realHash, "ni modifié");

        verify.Users.Where(u => u.PasswordHash == DemoWeakHash).Should().OnlyContain(u => !u.IsActive);
        verify.Customers.Should().BeEmpty("aucune donnée de démonstration n'est créée en production");
    }

    /// <summary>
    /// Revue ciblée avant commit — variante non testée jusqu'ici : un administrateur RÉEL (hash fort) occupe déjà
    /// le login bootstrap (« admin »), et AUCUN compte faible ne porte ce login (seuls des comptes faibles sous
    /// D'AUTRES logins subsistent). <c>SecureBootstrap</c> choisit alors <c>weakAccounts.FirstOrDefault()</c> —
    /// un compte faible ARBITRAIRE — comme compte à « sécuriser » et le renomme vers le login bootstrap. Ce test
    /// prouve que ce renommage ne doit JAMAIS entrer en collision avec l'administrateur réel déjà présent sous
    /// ce login, ni écraser silencieusement son identité.
    /// </summary>
    [Fact]
    public void Production_WithBootstrapSecret_RealAdminAlreadyOwnsBootstrapLogin_DoesNotCollideOrRenameArbitraryWeakAccount()
    {
        var dbPath = CreateMigratedDatabase();

        var realHash = BCrypt.Net.BCrypt.HashPassword("R3alStrongPass", 11);
        using (var context = CreateContext(dbPath))
        {
            // L'administrateur RÉEL occupe déjà le login bootstrap par défaut ("admin"), hash fort.
            context.Users.Add(new User
            {
                Username = "admin",
                NormalizedUsername = UserIdentityPolicy.NormalizeUsername("admin"),
                PasswordHash = realHash,
                FirstName = "Vrai", LastName = "Admin", Role = UserRole.Admin, IsActive = true,
            });
            // Compte faible, mais sous un AUTRE login — aucun compte faible ne porte "admin" ici.
            context.Users.Add(new User
            {
                Username = "marie.optic",
                NormalizedUsername = UserIdentityPolicy.NormalizeUsername("marie.optic"),
                PasswordHash = DemoWeakHash,
                FirstName = "Demo", LastName = "marie.optic", Role = UserRole.Optician, IsActive = true,
            });
            context.SaveChanges();
        }
        SqliteConnection.ClearAllPools();

        var options = new SeedOptions
        {
            Environment = ApplicationEnvironment.Production,
            BootstrapAdminPassword = "Str0ngBootstrapSecret",
        };

        using (var context = CreateContext(dbPath))
        {
            var act = () => new DatabaseSeeder().Seed(context, options);
            act.Should().NotThrow("le seed ne doit jamais planter au démarrage sur une collision de login évitable");
        }
        SqliteConnection.ClearAllPools();

        using var verify = CreateContext(dbPath);

        var realAdmin = verify.Users.Single(u => u.NormalizedUsername == "admin");
        realAdmin.PasswordHash.Should().Be(realHash, "l'administrateur réel ne doit jamais être ré-haché");
        realAdmin.IsActive.Should().BeTrue();
        realAdmin.LastName.Should().Be("Admin", "son identité ne doit jamais être écrasée par un renommage");

        var demoAccount = verify.Users.Single(u => u.PasswordHash == DemoWeakHash || u.NormalizedUsername == "marie.optic");
        demoAccount.NormalizedUsername.Should().Be("marie.optic",
            "le compte faible ne doit jamais être renommé vers le login d'un administrateur réel déjà présent");
        demoAccount.IsActive.Should().BeFalse("neutralisé comme tout compte à hash faible connu");

        verify.Users.Should().HaveCount(2, "aucun compte supplémentaire créé, aucun perdu");
    }
}
