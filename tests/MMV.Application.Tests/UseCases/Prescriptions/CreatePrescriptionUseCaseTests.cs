using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Prescriptions.CreatePrescription;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Prescriptions;

/// <summary>
/// P2C-4 — Use case « Créer une ordonnance » (<see cref="CreatePrescriptionUseCase"/>), extrait iso-fonctionnellement
/// de <c>PrescriptionFormViewModel.SavePrescriptionAsync</c> (qui appelait directement
/// <c>IPrescriptionRepository.CreateAsync</c> + <c>IUnitOfWork.CommitAsync</c>).
///
/// Prouve, sur un <b>vrai SQLite temporaire</b> (jamais le provider InMemory), que le use case :
/// <list type="number">
///   <item>crée une ordonnance et persiste (identifiant attribué, champs round-trip) ;</item>
///   <item>ne normalise pas les champs optionnels (comportement d'origine) ;</item>
///   <item>commande nulle : <see cref="ArgumentNullException"/> ;</item>
///   <item>dépendances critiques manquantes : le constructeur rejette <c>null</c>.</item>
/// </list>
/// </summary>
public sealed class CreatePrescriptionUseCaseTests : IDisposable
{
    private readonly string _workDirectory;

    public CreatePrescriptionUseCaseTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p2c4-create-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDirectory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_workDirectory))
                Directory.Delete(_workDirectory, recursive: true);
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

    private static CreatePrescriptionUseCase CreateUseCase(OpticDbContext context)
        => new(new PrescriptionRepository(context), new UnitOfWork(context));

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
            CreatedAt = DateTime.UtcNow.AddDays(-10),
            UpdatedAt = DateTime.UtcNow.AddDays(-10)
        };
        context.Customers.Add(customer);
        context.SaveChanges();
        return customer.CustomerId;
    }

    private static CreatePrescriptionCommand SampleCommand(long customerId) => new()
    {
        CustomerId = customerId,
        IssueDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
        DoctorName = "Dr House",
        OdSphere = -1.25,
        OdCylinder = -0.5,
        OdAxis = 90,
        OdAddition = 1.0,
        OdPrismValue = 2.0,
        OdPrismBase = PrismBase.In,
        OdVisualAcuity = "10/10",
        OgSphere = -1.0,
        OgCylinder = -0.25,
        OgAxis = 85,
        OgAddition = 1.0,
        OgPrismValue = 1.5,
        OgPrismBase = PrismBase.Out,
        OgVisualAcuity = "9/10",
        Notes = "Première paire"
    };

    // ------------------------------------------------------------------
    // (1) Création nominale : ordonnance persistée, champs round-trip
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_CreatesPrescription_AndPersists()
    {
        var dbPath = PathFor("create.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);

        CreatePrescriptionResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            result = await useCase.ExecuteAsync(SampleCommand(customerId));
        }

        result.PrescriptionId.Should().BeGreaterThan(0);

        using var verify = CreateContext(dbPath);
        var stored = verify.Prescriptions.AsNoTracking().Single();
        stored.CustomerId.Should().Be(customerId);
        stored.DoctorName.Should().Be("Dr House");
        stored.OdSphere.Should().Be(-1.25);
        stored.OdAxis.Should().Be(90);
        stored.OdPrismBase.Should().Be(PrismBase.In);
        stored.OgVisualAcuity.Should().Be("9/10");
        stored.Notes.Should().Be("Première paire");
        stored.IssueDate.Should().Be(new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc));
    }

    // ------------------------------------------------------------------
    // (2) Pas de normalisation des champs optionnels (comportement d'origine)
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_DoesNotNormalizeOptionalFields()
    {
        var dbPath = PathFor("no-normalize.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);

        var command = new CreatePrescriptionCommand
        {
            CustomerId = customerId,
            IssueDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            DoctorName = "   ", // blanc : conservé tel quel (le flux d'origine ne normalisait pas)
            Notes = string.Empty
        };

        using (var context = CreateContext(dbPath))
        {
            await CreateUseCase(context).ExecuteAsync(command);
        }

        using var verify = CreateContext(dbPath);
        var stored = verify.Prescriptions.AsNoTracking().Single();
        stored.DoctorName.Should().Be("   ", "le use case n'introduit pas de normalisation blanc → null");
        stored.Notes.Should().Be(string.Empty);
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
    // (4) Dépendances critiques manquantes → le constructeur rejette null
    // ------------------------------------------------------------------

    [Fact]
    public void Constructor_WithoutRepository_Throws()
    {
        var dbPath = PathFor("ctor-repo.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);

        Action act = () => _ = new CreatePrescriptionUseCase(prescriptionRepository: null!, new UnitOfWork(context));
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithoutUnitOfWork_Throws()
    {
        var dbPath = PathFor("ctor-uow.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);

        Action act = () => _ = new CreatePrescriptionUseCase(new PrescriptionRepository(context), unitOfWork: null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
