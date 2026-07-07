using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Customers.UpdateCustomer;
using MMV.Domain.Entities;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Customers;

/// <summary>
/// P2C-2 — Use case « Modifier un client » (<see cref="UpdateCustomerUseCase"/>), extrait iso-fonctionnellement de
/// la branche édition de <c>CustomerFormViewModel.ExecuteSave</c> (et du code-behind client).
///
/// Prouve, sur un <b>vrai SQLite temporaire</b> (jamais le provider InMemory), que le use case :
/// <list type="number">
///   <item>met à jour les champs d'un client existant, réaffecte <c>UpdatedAt</c> et préserve <c>CreatedAt</c> ;</item>
///   <item>normalise les champs optionnels blancs vers <c>null</c> ;</item>
///   <item>client introuvable : renvoie <c>CustomerFound = false</c> sans écrire ;</item>
///   <item>commande nulle : <see cref="ArgumentNullException"/> ;</item>
///   <item>dépendance critique manquante : le constructeur rejette <c>null</c>.</item>
/// </list>
/// Réutilise les mêmes repositories que le flux d'origine, partageant un unique <see cref="OpticDbContext"/>.
/// </summary>
public sealed class UpdateCustomerUseCaseTests : IDisposable
{
    private readonly string _workDirectory;

    public UpdateCustomerUseCaseTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p2c2-update-" + Guid.NewGuid().ToString("N"));
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

    private static UpdateCustomerUseCase CreateUseCase(OpticDbContext context)
    {
        return new UpdateCustomerUseCase(new CustomerRepository(context), new UnitOfWork(context));
    }

    private static void EnsureSchema(string databasePath)
    {
        using var context = CreateContext(databasePath);
        context.Database.EnsureCreated();
    }

    private static long SeedCustomer(string databasePath, DateTime createdAt)
    {
        using var context = CreateContext(databasePath);
        var customer = new Customer
        {
            FirstName = "Jean",
            LastName = "Dupont",
            Email = "jean@example.com",
            Phone = "0600000000",
            City = "Paris",
            Notes = "Notes initiales",
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };
        context.Customers.Add(customer);
        context.SaveChanges();
        return customer.CustomerId;
    }

    // ------------------------------------------------------------------
    // (1) Édition nominale : champs mis à jour, UpdatedAt réaffecté, CreatedAt préservé
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_UpdatesExistingCustomer_PreservesCreatedAt()
    {
        var dbPath = PathFor("update.db");
        EnsureSchema(dbPath);
        var createdAt = DateTime.UtcNow.AddDays(-30);
        var customerId = SeedCustomer(dbPath, createdAt);

        UpdateCustomerResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            result = await useCase.ExecuteAsync(new UpdateCustomerCommand
            {
                CustomerId = customerId,
                FirstName = "Jean",
                LastName = "Durand",
                Email = "jean.durand@example.com",
                Phone = "0611111111",
                City = "Lyon"
            });
        }

        result.CustomerFound.Should().BeTrue();
        result.CustomerId.Should().Be(customerId);
        result.DisplayName.Should().Be("Jean Durand");

        using var verify = CreateContext(dbPath);
        var customer = verify.Customers.AsNoTracking().Single();
        customer.LastName.Should().Be("Durand");
        customer.Email.Should().Be("jean.durand@example.com");
        customer.Phone.Should().Be("0611111111");
        customer.City.Should().Be("Lyon");
        customer.CreatedAt.Should().BeCloseTo(createdAt, TimeSpan.FromSeconds(1), "l'édition ne touche pas CreatedAt");
        customer.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1), "UpdatedAt est réaffecté");
    }

    // ------------------------------------------------------------------
    // (2) Champs optionnels blancs → null
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_BlankOptionalFields_StoredAsNull()
    {
        var dbPath = PathFor("blank.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath, DateTime.UtcNow.AddDays(-5));

        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            await useCase.ExecuteAsync(new UpdateCustomerCommand
            {
                CustomerId = customerId,
                FirstName = "Jean",
                LastName = "Dupont",
                Email = "   ",
                Phone = "",
                Notes = "  "
            });
        }

        using var verify = CreateContext(dbPath);
        var customer = verify.Customers.AsNoTracking().Single();
        customer.Email.Should().BeNull("les champs optionnels blancs sont normalisés en null, comme le flux d'origine");
        customer.Phone.Should().BeNull();
        customer.Notes.Should().BeNull();
    }

    // ------------------------------------------------------------------
    // (3) Client introuvable → CustomerFound = false, aucune écriture
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_CustomerNotFound_ReturnsNotFound_AndDoesNotWrite()
    {
        var dbPath = PathFor("missing.db");
        EnsureSchema(dbPath);

        UpdateCustomerResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            result = await useCase.ExecuteAsync(new UpdateCustomerCommand
            {
                CustomerId = 4242,
                FirstName = "Ghost",
                LastName = "User"
            });
        }

        result.CustomerFound.Should().BeFalse();
        result.CustomerId.Should().Be(4242);

        using var verify = CreateContext(dbPath);
        verify.Customers.AsNoTracking().Should().BeEmpty("aucun client n'est créé quand l'id est introuvable");
    }

    // ------------------------------------------------------------------
    // (P3-1) Validation — prénom vide sur client existant → échec, ligne inchangée
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_BlankFirstName_FailsValidation_AndDoesNotWrite()
    {
        var dbPath = PathFor("no-firstname.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath, DateTime.UtcNow.AddDays(-10));

        UpdateCustomerResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            result = await useCase.ExecuteAsync(new UpdateCustomerCommand
            {
                CustomerId = customerId,
                FirstName = "   ",
                LastName = "Durand"
            });
        }

        result.CustomerFound.Should().BeTrue("le client existe : ce n'est pas un NotFound mais une validation refusée");
        result.IsValid.Should().BeFalse();
        result.ValidationErrors.Should().Contain(e => e.PropertyName == "FirstName");

        using var verify = CreateContext(dbPath);
        var customer = verify.Customers.AsNoTracking().Single();
        customer.FirstName.Should().Be("Jean", "aucune écriture : la ligne conserve ses valeurs initiales");
        customer.LastName.Should().Be("Dupont");
    }

    // ------------------------------------------------------------------
    // (P3-1) Validation — nom vide → échec, ligne inchangée
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_BlankLastName_FailsValidation_AndDoesNotWrite()
    {
        var dbPath = PathFor("no-lastname.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath, DateTime.UtcNow.AddDays(-10));

        UpdateCustomerResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            result = await useCase.ExecuteAsync(new UpdateCustomerCommand
            {
                CustomerId = customerId,
                FirstName = "Jean",
                LastName = ""
            });
        }

        result.CustomerFound.Should().BeTrue();
        result.IsValid.Should().BeFalse();
        result.ValidationErrors.Should().Contain(e => e.PropertyName == "LastName");

        using var verify = CreateContext(dbPath);
        verify.Customers.AsNoTracking().Single().LastName.Should().Be("Dupont");
    }

    // ------------------------------------------------------------------
    // (P3-1) Validation — email renseigné mais invalide → échec, ligne inchangée
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_InvalidEmail_FailsValidation_AndDoesNotWrite()
    {
        var dbPath = PathFor("bad-email.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath, DateTime.UtcNow.AddDays(-10));

        UpdateCustomerResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            result = await useCase.ExecuteAsync(new UpdateCustomerCommand
            {
                CustomerId = customerId,
                FirstName = "Jean",
                LastName = "Durand",
                Email = "pas-un-email"
            });
        }

        result.CustomerFound.Should().BeTrue();
        result.IsValid.Should().BeFalse();
        result.ValidationErrors.Should().Contain(e => e.PropertyName == "Email");

        using var verify = CreateContext(dbPath);
        var customer = verify.Customers.AsNoTracking().Single();
        customer.Email.Should().Be("jean@example.com", "aucune écriture : l'email initial est conservé");
        customer.LastName.Should().Be("Dupont");
    }

    // ------------------------------------------------------------------
    // (4) Commande nulle → ArgumentNullException
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_NullCommand_Throws()
    {
        var dbPath = PathFor("null.db");
        EnsureSchema(dbPath);

        using var context = CreateContext(dbPath);
        var useCase = CreateUseCase(context);

        Func<Task> act = () => useCase.ExecuteAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ------------------------------------------------------------------
    // (5) Dépendance critique manquante → le constructeur rejette null
    // ------------------------------------------------------------------

    [Fact]
    public void Constructor_WithoutUnitOfWork_Throws()
    {
        var dbPath = PathFor("ctor.db");
        EnsureSchema(dbPath);

        using var context = CreateContext(dbPath);

        Action act = () => _ = new UpdateCustomerUseCase(new CustomerRepository(context), unitOfWork: null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
