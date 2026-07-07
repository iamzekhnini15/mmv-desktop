using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Customers.ListCustomersForPicker;
using MMV.Domain.Entities;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Customers;

/// <summary>
/// P2D-6 — Query use case « Lister les clients pour un sélecteur » (<see cref="ListCustomersForPickerUseCase"/>).
/// Vérifie sur un <b>vrai SQLite temporaire</b> (jamais InMemory) : la projection vers des
/// <see cref="CustomerPickerItemDto"/> plats (jamais l'entité EF <c>Customer</c>), le portage des champs consommés
/// par le formulaire de commande, le tri par nom, le cas vide et les gardes.
/// </summary>
public sealed class ListCustomersForPickerUseCaseTests : IDisposable
{
    private readonly string _workDirectory;

    public ListCustomersForPickerUseCaseTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p2d6-customers-" + Guid.NewGuid().ToString("N"));
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
    public async Task ListCustomers_ReturnsProjectedDtos_SortedByLastName()
    {
        var dbPath = PathFor("list.db");
        EnsureSchema(dbPath);

        using (var context = CreateContext(dbPath))
        {
            context.Customers.Add(new Customer { FirstName = "Zoé", LastName = "Zulu", Phone = "0102", Email = "z@ex.fr" });
            context.Customers.Add(new Customer { FirstName = "Alice", LastName = "Alpha", Phone = "0304", Email = "a@ex.fr" });
            context.SaveChanges();
        }

        IReadOnlyList<CustomerPickerItemDto> result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = new ListCustomersForPickerUseCase(new CustomerRepository(context));
            result = await useCase.ExecuteAsync(new ListCustomersForPickerQuery());
        }

        result.Should().HaveCount(2);
        result.Should().AllBeOfType<CustomerPickerItemDto>();
        // Tri par nom (comme OrderFormViewModel.InitializeAsync).
        result[0].LastName.Should().Be("Alpha");
        result[1].LastName.Should().Be("Zulu");
        // Portage fidèle des champs consommés par le sélecteur.
        result[0].FirstName.Should().Be("Alice");
        result[0].Phone.Should().Be("0304");
        result[0].Email.Should().Be("a@ex.fr");
        result[0].CustomerId.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ListCustomers_NoData_ReturnsEmpty()
    {
        var dbPath = PathFor("empty.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);
        var result = await new ListCustomersForPickerUseCase(new CustomerRepository(context))
            .ExecuteAsync(new ListCustomersForPickerQuery());
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task NullQuery_Throws()
    {
        var dbPath = PathFor("null.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);
        await ((Func<Task>)(() => new ListCustomersForPickerUseCase(new CustomerRepository(context)).ExecuteAsync(null!)))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_RejectsNullRepository()
    {
        ((Action)(() => _ = new ListCustomersForPickerUseCase(null!))).Should().Throw<ArgumentNullException>();
    }
}
