using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Customers.DeleteCustomer;
using MMV.Domain.Entities;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Customers;

/// <summary>
/// P2C-3 — Use case « Supprimer un client » (<see cref="DeleteCustomerUseCase"/>), extrait iso-fonctionnellement de
/// <c>CustomersListViewModel.ExecuteDelete</c> (qui appelait directement <c>ICustomerRepository.DeleteAsync</c> +
/// <c>IUnitOfWork.SaveChangesAsync</c>).
///
/// Prouve, sur un <b>vrai SQLite temporaire</b> (jamais le provider InMemory), que le use case :
/// <list type="number">
///   <item>supprime un client existant et persiste (CustomerFound = true) ;</item>
///   <item>client introuvable : renvoie <c>CustomerFound = false</c> sans écrire ;</item>
///   <item>commande nulle : <see cref="ArgumentNullException"/> ;</item>
///   <item>dépendances critiques manquantes : le constructeur rejette <c>null</c>.</item>
/// </list>
/// Réutilise les mêmes repositories que le flux d'origine, partageant un unique <see cref="OpticDbContext"/>.
/// </summary>
public sealed class DeleteCustomerUseCaseTests : IDisposable
{
    private readonly string _workDirectory;

    public DeleteCustomerUseCaseTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p2c3-delete-" + Guid.NewGuid().ToString("N"));
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

    private static DeleteCustomerUseCase CreateUseCase(OpticDbContext context)
    {
        return new DeleteCustomerUseCase(new CustomerRepository(context), new UnitOfWork(context));
    }

    private static void EnsureSchema(string databasePath)
    {
        using var context = CreateContext(databasePath);
        context.Database.EnsureCreated();
    }

    private static long SeedCustomer(string databasePath)
    {
        using var context = CreateContext(databasePath);
        var customer = new Customer
        {
            FirstName = "Jean",
            LastName = "Dupont",
            Email = "jean@example.com",
            Phone = "0600000000",
            City = "Paris",
            CreatedAt = DateTime.UtcNow.AddDays(-10),
            UpdatedAt = DateTime.UtcNow.AddDays(-10)
        };
        context.Customers.Add(customer);
        context.SaveChanges();
        return customer.CustomerId;
    }

    // ------------------------------------------------------------------
    // (1) Suppression nominale : le client existant est supprimé et persisté
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_DeletesExistingCustomer_AndPersists()
    {
        var dbPath = PathFor("delete.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);

        DeleteCustomerResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            result = await useCase.ExecuteAsync(new DeleteCustomerCommand { CustomerId = customerId });
        }

        result.CustomerFound.Should().BeTrue();
        result.CustomerId.Should().Be(customerId);

        using var verify = CreateContext(dbPath);
        verify.Customers.AsNoTracking().Should().BeEmpty("le client supprimé n'est plus persisté");
    }

    // ------------------------------------------------------------------
    // (2) Client introuvable → CustomerFound = false, aucune écriture
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_CustomerNotFound_ReturnsNotFound_AndDoesNotWrite()
    {
        var dbPath = PathFor("missing.db");
        EnsureSchema(dbPath);
        var survivorId = SeedCustomer(dbPath);

        DeleteCustomerResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            result = await useCase.ExecuteAsync(new DeleteCustomerCommand { CustomerId = 4242 });
        }

        result.CustomerFound.Should().BeFalse();
        result.CustomerId.Should().Be(4242);

        using var verify = CreateContext(dbPath);
        verify.Customers.AsNoTracking().Should().ContainSingle(c => c.CustomerId == survivorId,
            "aucun client n'est supprimé quand l'id est introuvable");
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
    public void Constructor_WithoutRepository_Throws()
    {
        var dbPath = PathFor("ctor-repo.db");
        EnsureSchema(dbPath);

        using var context = CreateContext(dbPath);

        Action act = () => _ = new DeleteCustomerUseCase(customerRepository: null!, new UnitOfWork(context));

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithoutUnitOfWork_Throws()
    {
        var dbPath = PathFor("ctor-uow.db");
        EnsureSchema(dbPath);

        using var context = CreateContext(dbPath);

        Action act = () => _ = new DeleteCustomerUseCase(new CustomerRepository(context), unitOfWork: null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
