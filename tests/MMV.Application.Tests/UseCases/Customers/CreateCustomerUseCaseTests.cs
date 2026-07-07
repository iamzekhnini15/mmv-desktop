using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Customers.CreateCustomer;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Customers;

/// <summary>
/// P2C-2 — Use case « Créer un client » (<see cref="CreateCustomerUseCase"/>), extrait iso-fonctionnellement de la
/// branche création de <c>CustomerFormViewModel.ExecuteSave</c> (et du code-behind client).
///
/// Prouve, sur un <b>vrai SQLite temporaire</b> (jamais le provider InMemory), que le use case :
/// <list type="number">
///   <item>crée et persiste un client avec ses champs, attribue un identifiant et renvoie un nom d'affichage ;</item>
///   <item>normalise les champs optionnels blancs vers <c>null</c> (comme le flux d'origine) ;</item>
///   <item>commande nulle : <see cref="ArgumentNullException"/> ;</item>
///   <item>dépendance critique manquante : le constructeur rejette <c>null</c>.</item>
/// </list>
/// Réutilise les mêmes repositories que le flux d'origine, partageant un unique <see cref="OpticDbContext"/>.
/// </summary>
public sealed class CreateCustomerUseCaseTests : IDisposable
{
    private readonly string _workDirectory;

    public CreateCustomerUseCaseTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p2c2-create-" + Guid.NewGuid().ToString("N"));
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

    private static CreateCustomerUseCase CreateUseCase(OpticDbContext context)
    {
        return new CreateCustomerUseCase(new CustomerRepository(context), new UnitOfWork(context));
    }

    private static void EnsureSchema(string databasePath)
    {
        using var context = CreateContext(databasePath);
        context.Database.EnsureCreated();
    }

    // ------------------------------------------------------------------
    // (1) Création nominale : client persisté, identifiant et nom d'affichage renvoyés
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_CreatesAndPersistsCustomer()
    {
        var dbPath = PathFor("create.db");
        EnsureSchema(dbPath);

        CreateCustomerResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            result = await useCase.ExecuteAsync(new CreateCustomerCommand
            {
                FirstName = "Jean",
                LastName = "Dupont",
                Email = "jean.dupont@example.com",
                Phone = "0612345678",
                City = "Paris",
                PostalCode = "75000"
            });
        }

        result.CustomerId.Should().BeGreaterThan(0);
        result.DisplayName.Should().Be("Jean Dupont");

        using var verify = CreateContext(dbPath);
        var customer = verify.Customers.AsNoTracking().Single();
        customer.FirstName.Should().Be("Jean");
        customer.LastName.Should().Be("Dupont");
        customer.Email.Should().Be("jean.dupont@example.com");
        customer.Phone.Should().Be("0612345678");
        customer.City.Should().Be("Paris");
        customer.PostalCode.Should().Be("75000");
        customer.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        customer.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    // ------------------------------------------------------------------
    // (2) Champs optionnels blancs → null
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_BlankOptionalFields_StoredAsNull()
    {
        var dbPath = PathFor("blank.db");
        EnsureSchema(dbPath);

        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            await useCase.ExecuteAsync(new CreateCustomerCommand
            {
                FirstName = "Marie",
                LastName = "Martin",
                Email = "   ",
                Phone = "",
                Address = "  ",
                City = null,
                Notes = "   "
            });
        }

        using var verify = CreateContext(dbPath);
        var customer = verify.Customers.AsNoTracking().Single();
        customer.Email.Should().BeNull("les champs optionnels blancs sont normalisés en null, comme le flux d'origine");
        customer.Phone.Should().BeNull();
        customer.Address.Should().BeNull();
        customer.City.Should().BeNull();
        customer.Notes.Should().BeNull();
    }

    // ------------------------------------------------------------------
    // (P3-1) Validation de commande — prénom vide → échec, aucune écriture
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_BlankFirstName_FailsValidation_AndDoesNotWrite()
    {
        var dbPath = PathFor("no-firstname.db");
        EnsureSchema(dbPath);

        CreateCustomerResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            result = await useCase.ExecuteAsync(new CreateCustomerCommand
            {
                FirstName = "   ",
                LastName = "Dupont"
            });
        }

        result.IsValid.Should().BeFalse();
        result.CustomerId.Should().Be(0, "une commande invalide ne persiste rien");
        result.ValidationErrors.Should().Contain(e => e.PropertyName == "FirstName");

        using var verify = CreateContext(dbPath);
        verify.Customers.AsNoTracking().Should().BeEmpty("aucun client n'est créé quand la commande est invalide");
    }

    // ------------------------------------------------------------------
    // (P3-1) Validation de commande — nom vide → échec, aucune écriture
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_BlankLastName_FailsValidation_AndDoesNotWrite()
    {
        var dbPath = PathFor("no-lastname.db");
        EnsureSchema(dbPath);

        CreateCustomerResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            result = await useCase.ExecuteAsync(new CreateCustomerCommand
            {
                FirstName = "Jean",
                LastName = ""
            });
        }

        result.IsValid.Should().BeFalse();
        result.ValidationErrors.Should().Contain(e => e.PropertyName == "LastName");

        using var verify = CreateContext(dbPath);
        verify.Customers.AsNoTracking().Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // (P3-1) Validation de commande — email renseigné mais invalide → échec
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_InvalidEmail_FailsValidation_AndDoesNotWrite()
    {
        var dbPath = PathFor("bad-email.db");
        EnsureSchema(dbPath);

        CreateCustomerResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            result = await useCase.ExecuteAsync(new CreateCustomerCommand
            {
                FirstName = "Jean",
                LastName = "Dupont",
                Email = "pas-un-email"
            });
        }

        result.IsValid.Should().BeFalse();
        result.ValidationErrors.Should().Contain(e => e.PropertyName == "Email");

        using var verify = CreateContext(dbPath);
        verify.Customers.AsNoTracking().Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // (P3-1) Email vide/blanc reste autorisé (comportement existant conservé)
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_BlankEmail_RemainsValid_AndPersists()
    {
        var dbPath = PathFor("blank-email.db");
        EnsureSchema(dbPath);

        CreateCustomerResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            result = await useCase.ExecuteAsync(new CreateCustomerCommand
            {
                FirstName = "Jean",
                LastName = "Dupont",
                Email = "   "
            });
        }

        result.IsValid.Should().BeTrue("un email vide reste autorisé — comportement existant conservé");
        result.CustomerId.Should().BeGreaterThan(0);

        using var verify = CreateContext(dbPath);
        verify.Customers.AsNoTracking().Single().Email.Should().BeNull();
    }

    // ------------------------------------------------------------------
    // (3) Commande nulle → ArgumentNullException
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
    // (4) Dépendance critique manquante → le constructeur rejette null
    // ------------------------------------------------------------------

    [Fact]
    public void Constructor_WithoutCustomerRepository_Throws()
    {
        var dbPath = PathFor("ctor.db");
        EnsureSchema(dbPath);

        using var context = CreateContext(dbPath);

        Action act = () => _ = new CreateCustomerUseCase(customerRepository: null!, new UnitOfWork(context));

        act.Should().Throw<ArgumentNullException>();
    }
}
