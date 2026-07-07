using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Customers.ListCustomers;
using MMV.Domain.Entities;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Customers;

/// <summary>
/// P2D-7C — Query use case « Lister les clients » (<see cref="ListCustomersUseCase"/>). Vérifie sur un
/// <b>vrai SQLite temporaire</b> (jamais InMemory) : la projection vers des <see cref="CustomerListItemDto"/> plats
/// (jamais l'entité EF <c>Customer</c>), le portage fidèle de tous les champs consommés par la liste / la fiche /
/// le formulaire d'édition, le cas vide et les gardes (query nulle, repository nul).
/// </summary>
public sealed class ListCustomersUseCaseTests : IDisposable
{
    private readonly string _workDirectory;

    public ListCustomersUseCaseTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p2d7-customers-" + Guid.NewGuid().ToString("N"));
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
    {
        var options = new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False").Options;
        return new OpticDbContext(options);
    }

    private static void EnsureSchema(string databasePath)
    {
        using var context = CreateContext(databasePath);
        context.Database.EnsureCreated();
    }

    [Fact]
    public async Task ListCustomers_ProjectsAllScalarFields_ToFlatDtos()
    {
        var dbPath = PathFor("list.db");
        EnsureSchema(dbPath);

        var birth = new DateTime(1980, 5, 3);
        using (var context = CreateContext(dbPath))
        {
            context.Customers.Add(new Customer
            {
                FirstName = "Alice",
                LastName = "Alpha",
                BirthDate = birth,
                Phone = "0304",
                Email = "a@ex.fr",
                Address = "1 rue A",
                City = "Lyon",
                PostalCode = "69001",
                SocialSecurityNumber = "1801",
                InsuranceName = "Mutuelle A",
                Notes = "VIP",
            });
            context.Customers.Add(new Customer { FirstName = "Bob", LastName = "Bravo", Phone = "0506" });
            context.SaveChanges();
        }

        IReadOnlyList<CustomerListItemDto> result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = new ListCustomersUseCase(new CustomerRepository(context));
            result = await useCase.ExecuteAsync(new ListCustomersQuery());
        }

        result.Should().HaveCount(2);
        result.Should().AllBeOfType<CustomerListItemDto>();

        var alice = result.Single(c => c.LastName == "Alpha");
        alice.CustomerId.Should().BeGreaterThan(0);
        alice.FirstName.Should().Be("Alice");
        alice.BirthDate.Should().Be(birth);
        alice.Phone.Should().Be("0304");
        alice.Email.Should().Be("a@ex.fr");
        alice.Address.Should().Be("1 rue A");
        alice.City.Should().Be("Lyon");
        alice.PostalCode.Should().Be("69001");
        alice.SocialSecurityNumber.Should().Be("1801");
        alice.InsuranceName.Should().Be("Mutuelle A");
        alice.Notes.Should().Be("VIP");
    }

    [Fact]
    public async Task ListCustomers_DoesNotReturnDomainEntities()
    {
        var dbPath = PathFor("no-entity.db");
        EnsureSchema(dbPath);
        using (var context = CreateContext(dbPath))
        {
            context.Customers.Add(new Customer { FirstName = "Zoé", LastName = "Zulu" });
            context.SaveChanges();
        }

        using var ctx = CreateContext(dbPath);
        var result = await new ListCustomersUseCase(new CustomerRepository(ctx)).ExecuteAsync(new ListCustomersQuery());

        result.Should().ContainSingle().Which.Should().BeOfType<CustomerListItemDto>();
        result.Should().NotContain(dto => (object)dto is Customer);
    }

    [Fact]
    public async Task ListCustomers_NoData_ReturnsEmpty()
    {
        var dbPath = PathFor("empty.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);
        var result = await new ListCustomersUseCase(new CustomerRepository(context)).ExecuteAsync(new ListCustomersQuery());
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task NullQuery_Throws()
    {
        var dbPath = PathFor("null.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);
        await ((Func<Task>)(() => new ListCustomersUseCase(new CustomerRepository(context)).ExecuteAsync(null!)))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_RejectsNullRepository()
    {
        ((Action)(() => _ = new ListCustomersUseCase(null!))).Should().Throw<ArgumentNullException>();
    }
}
