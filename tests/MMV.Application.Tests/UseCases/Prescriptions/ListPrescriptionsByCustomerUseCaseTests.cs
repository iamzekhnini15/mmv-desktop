using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Prescriptions.ListPrescriptionsByCustomer;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Prescriptions;

/// <summary>
/// P2D-5 — Query use case « Lister les ordonnances d'un client » (<see cref="ListPrescriptionsByCustomerUseCase"/>).
/// Vérifie sur un <b>vrai SQLite temporaire</b> (jamais InMemory) : la projection vers DTO plats (jamais d'entité EF),
/// le portage de tous les champs optométriques, le filtre par client, le tri décroissant par date d'émission hérité
/// du repository, le cas vide et les gardes.
/// </summary>
public sealed class ListPrescriptionsByCustomerUseCaseTests : IDisposable
{
    private readonly string _workDirectory;

    public ListPrescriptionsByCustomerUseCaseTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p2d5-prescriptions-" + Guid.NewGuid().ToString("N"));
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

    private static long SeedCustomer(OpticDbContext context, string firstName)
    {
        var customer = new Customer { FirstName = firstName, LastName = "Test" };
        context.Customers.Add(customer);
        context.SaveChanges();
        return customer.CustomerId;
    }

    [Fact]
    public async Task ListPrescriptions_ReturnsProjectedDtos_SortedByIssueDateDescending_FilteredByCustomer()
    {
        var dbPath = PathFor("list.db");
        EnsureSchema(dbPath);

        long customerId;
        using (var context = CreateContext(dbPath))
        {
            customerId = SeedCustomer(context, "Alice");
            var otherId = SeedCustomer(context, "Bob");

            context.Prescriptions.Add(new Prescription
            {
                CustomerId = customerId,
                IssueDate = new DateOnly(2026, 1, 1),
                DoctorName = "Dr Ancien",
                OdSphere = -1.25,
                OdCylinder = -0.5,
                OdAxis = 90,
                OdAddition = 1.0,
                OdPrismValue = 2.0,
                OdPrismBase = PrismBase.In,
                OdVisualAcuity = "10/10",
                OgSphere = -1.0,
                Notes = "RAS",
            });
            context.Prescriptions.Add(new Prescription
            {
                CustomerId = customerId,
                IssueDate = new DateOnly(2026, 6, 1),
                DoctorName = "Dr Récent",
            });
            // Ordonnance d'un AUTRE client : ne doit pas apparaître.
            context.Prescriptions.Add(new Prescription
            {
                CustomerId = otherId,
                IssueDate = new DateOnly(2026, 12, 1),
                DoctorName = "Dr Autre",
            });
            context.SaveChanges();
        }

        IReadOnlyList<PrescriptionListItemDto> result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = new ListPrescriptionsByCustomerUseCase(new PrescriptionRepository(context));
            result = await useCase.ExecuteAsync(new ListPrescriptionsByCustomerQuery { CustomerId = customerId });
        }

        result.Should().HaveCount(2);
        result.Should().AllBeOfType<PrescriptionListItemDto>();
        // Tri décroissant par date d'émission (hérité du repository) : le plus récent d'abord.
        result[0].DoctorName.Should().Be("Dr Récent");
        result[1].DoctorName.Should().Be("Dr Ancien");
        // Portage fidèle des champs optométriques (détail + pré-remplissage du formulaire).
        result[1].OdSphere.Should().Be(-1.25);
        result[1].OdCylinder.Should().Be(-0.5);
        result[1].OdAxis.Should().Be(90);
        result[1].OdAddition.Should().Be(1.0);
        result[1].OdPrismValue.Should().Be(2.0);
        result[1].OdPrismBase.Should().Be(PrismBase.In);
        result[1].OdVisualAcuity.Should().Be("10/10");
        result[1].OgSphere.Should().Be(-1.0);
        result[1].Notes.Should().Be("RAS");
        result[1].CustomerId.Should().Be(customerId);
    }

    [Fact]
    public async Task ListPrescriptions_NoData_ReturnsEmpty()
    {
        var dbPath = PathFor("empty.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);
        var result = await new ListPrescriptionsByCustomerUseCase(new PrescriptionRepository(context))
            .ExecuteAsync(new ListPrescriptionsByCustomerQuery { CustomerId = 54321 });
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task NullQuery_Throws()
    {
        var dbPath = PathFor("null.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);
        await ((Func<Task>)(() => new ListPrescriptionsByCustomerUseCase(new PrescriptionRepository(context)).ExecuteAsync(null!)))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_RejectsNullRepository()
    {
        ((Action)(() => _ = new ListPrescriptionsByCustomerUseCase(null!))).Should().Throw<ArgumentNullException>();
    }
}
