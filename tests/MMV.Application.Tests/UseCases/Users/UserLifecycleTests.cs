using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Users.SetUserActive;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Policies;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Repositories;
using MMV.Infrastructure.Services;
using Xunit;

namespace MMV.Application.Tests.UseCases.Users;

/// <summary>
/// P3-10 — Cycle de vie d'un compte : désactivation, réactivation, idempotence, effet sur l'authentification, et
/// préservation de l'historique. La désactivation reste le SEUL mécanisme de retrait : aucune suppression
/// physique n'est exposée (aucun <c>DeleteUserUseCase</c> n'existe).
/// </summary>
public sealed class UserLifecycleTests : IDisposable
{
    private readonly string _workDirectory;

    public UserLifecycleTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p310-life-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDirectory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_workDirectory)) Directory.Delete(_workDirectory, recursive: true); }
        catch { /* best-effort */ }
    }

    private string PathFor(string fileName) => Path.Combine(_workDirectory, fileName);

    private static OpticDbContext CreateContext(string databasePath)
        => new(new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False").Options);

    private static void EnsureSchema(string databasePath)
    {
        using var context = CreateContext(databasePath);
        context.Database.EnsureCreated();
    }

    private static async Task<long> SeedUserAsync(string dbPath, string username, string password)
    {
        using var context = CreateContext(dbPath);
        var auth = new AuthenticationService(new UnitOfWork(context));
        var created = await new UserRepository(context).CreateAsync(new User
        {
            Username = username,
            NormalizedUsername = UserIdentityPolicy.NormalizeUsername(username),
            FirstName = "F", LastName = "L", Role = UserRole.Optician, IsActive = true,
            PasswordHash = auth.HashPassword(password),
        });
        await new UnitOfWork(context).SaveChangesAsync();
        return created.UserId;
    }

    private static async Task<bool> SetActiveAsync(string dbPath, long userId, bool isActive)
    {
        using var context = CreateContext(dbPath);
        var result = await new SetUserActiveUseCase(new UserRepository(context), new UnitOfWork(context))
            .ExecuteAsync(new SetUserActiveCommand { UserId = userId, IsActive = isActive });
        return result.UserFound;
    }

    /// <summary>
    /// L'état est une valeur ABSOLUE, pas une bascule : répéter la même demande est idempotent, ce qui rend deux
    /// postes demandant la même désactivation inoffensifs l'un pour l'autre.
    /// </summary>
    [Fact]
    public async Task Deactivate_ThenDeactivateAgain_IsIdempotent()
    {
        var dbPath = PathFor("deactivate-idempotent.db");
        EnsureSchema(dbPath);
        var id = await SeedUserAsync(dbPath, "marie.optic", "S3cretPass1");

        (await SetActiveAsync(dbPath, id, false)).Should().BeTrue();
        (await SetActiveAsync(dbPath, id, false)).Should().BeTrue();

        using var verify = CreateContext(dbPath);
        verify.Users.AsNoTracking().Single().IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Reactivate_ThenReactivateAgain_IsIdempotent()
    {
        var dbPath = PathFor("reactivate-idempotent.db");
        EnsureSchema(dbPath);
        var id = await SeedUserAsync(dbPath, "marie.optic", "S3cretPass1");

        await SetActiveAsync(dbPath, id, false);
        (await SetActiveAsync(dbPath, id, true)).Should().BeTrue();
        (await SetActiveAsync(dbPath, id, true)).Should().BeTrue();

        using var verify = CreateContext(dbPath);
        verify.Users.AsNoTracking().Single().IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task SetActive_UnknownUser_ReportsNotFound()
    {
        var dbPath = PathFor("setactive-unknown.db");
        EnsureSchema(dbPath);

        (await SetActiveAsync(dbPath, 4_242, false)).Should().BeFalse();
    }

    /// <summary>
    /// Un compte désactivé ne peut plus s'authentifier, et le redevient après réactivation. La lecture
    /// d'authentification étant fraîche (<c>AsNoTracking</c>), une désactivation faite ailleurs est vue
    /// immédiatement à la connexion suivante.
    /// </summary>
    [Fact]
    public async Task DeactivatedAccount_CannotAuthenticate_AndCanAgainAfterReactivation()
    {
        var dbPath = PathFor("deactivate-auth.db");
        EnsureSchema(dbPath);
        var id = await SeedUserAsync(dbPath, "marie.optic", "S3cretPass1");

        using (var context = CreateContext(dbPath))
        {
            var auth = new AuthenticationService(new UnitOfWork(context));
            (await auth.AuthenticateAsync("marie.optic", "S3cretPass1")).Should().NotBeNull();
        }

        await SetActiveAsync(dbPath, id, false);

        using (var context = CreateContext(dbPath))
        {
            var auth = new AuthenticationService(new UnitOfWork(context));
            (await auth.AuthenticateAsync("marie.optic", "S3cretPass1"))
                .Should().BeNull("un compte désactivé n'est pas authentifiable");

            // Anti-énumération : compte désactivé et mot de passe erroné donnent le MÊME résultat (null).
            (await auth.AuthenticateAsync("marie.optic", "WrongPass1")).Should().BeNull();
        }

        await SetActiveAsync(dbPath, id, true);

        using (var context = CreateContext(dbPath))
        {
            var auth = new AuthenticationService(new UnitOfWork(context));
            (await auth.AuthenticateAsync("MARIE.OPTIC", "S3cretPass1"))
                .Should().NotBeNull("la réactivation restaure l'accès, quelle que soit la casse saisie");
        }
    }

    /// <summary>
    /// La désactivation ne touche PAS l'historique : le vendeur d'une vente et l'auteur d'un mouvement de stock
    /// restent attribués. C'est ce qui justifie de conserver la désactivation plutôt que d'introduire une
    /// suppression (les FK sont `SetNull` : supprimer anonymiserait l'historique).
    /// </summary>
    [Fact]
    public async Task Deactivation_LeavesHistoricalAttributionIntact()
    {
        var dbPath = PathFor("deactivate-history.db");
        EnsureSchema(dbPath);
        var id = await SeedUserAsync(dbPath, "marie.optic", "S3cretPass1");

        using (var context = CreateContext(dbPath))
        {
            var supplier = new Supplier { Name = "Fournisseur" };
            context.Suppliers.Add(supplier);
            context.SaveChanges();

            var product = new Product
            {
                Reference = "REF-1", Name = "Monture", Category = ProductCategoryEnum.MONTURE,
                SupplierId = supplier.SupplierId, SalePrice = 10m,
            };
            context.Products.Add(product);
            context.SaveChanges();

            context.StockMovements.Add(new StockMovement
            {
                ProductId = product.ProductId, Quantity = 5, PerformedByUserId = id, CreatedAt = DateTime.UtcNow,
            });
            context.Sales.Add(new Sale { StaffId = id, SaleDate = DateTime.UtcNow, TotalAmount = 10m });
            context.SaveChanges();
        }

        await SetActiveAsync(dbPath, id, false);

        using var verify = CreateContext(dbPath);
        verify.Users.AsNoTracking().Single().IsActive.Should().BeFalse();
        verify.StockMovements.AsNoTracking().Single().PerformedByUserId
            .Should().Be(id, "l'auteur du mouvement reste attribué");
        verify.Sales.AsNoTracking().Single().StaffId
            .Should().Be(id, "le vendeur de la vente reste attribué");
    }

    /// <summary>
    /// Les sélecteurs excluent les comptes inactifs, tandis que la liste de gestion les conserve : désactiver
    /// retire de l'usage courant sans faire disparaître le compte de l'administration.
    /// </summary>
    [Fact]
    public async Task DeactivatedAccount_IsExcludedFromSelectors_ButStillListedForManagement()
    {
        var dbPath = PathFor("deactivate-selectors.db");
        EnsureSchema(dbPath);
        var id = await SeedUserAsync(dbPath, "marie.optic", "S3cretPass1");
        await SetActiveAsync(dbPath, id, false);

        using var context = CreateContext(dbPath);
        var repository = new UserRepository(context);

        (await repository.GetActiveUsersAsync()).Should().BeEmpty();
        (await repository.GetByRoleAsync(UserRole.Optician)).Should().BeEmpty();
        (await repository.GetAllAsync()).Should().ContainSingle();
    }
}
