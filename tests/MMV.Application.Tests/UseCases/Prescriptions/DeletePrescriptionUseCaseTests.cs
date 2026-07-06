using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Prescriptions.DeletePrescription;
using MMV.Domain.Entities;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Prescriptions;

/// <summary>
/// P2C-4 — Use case « Supprimer une ordonnance » (<see cref="DeletePrescriptionUseCase"/>), extrait
/// iso-fonctionnellement de <c>CustomerPrescriptionsViewModel.ExecuteDeleteAsync</c> (qui appelait directement
/// <c>IPrescriptionRepository.DeleteAsync(id)</c> + <c>IUnitOfWork.CommitAsync</c>).
///
/// Prouve, sur un <b>vrai SQLite temporaire</b> (jamais le provider InMemory), que le use case :
/// <list type="number">
///   <item>supprime une ordonnance existante et persiste (PrescriptionFound = true) ;</item>
///   <item>ordonnance introuvable : renvoie <c>PrescriptionFound = false</c> sans écrire ;</item>
///   <item>commande nulle : <see cref="ArgumentNullException"/> ;</item>
///   <item>dépendances critiques manquantes : le constructeur rejette <c>null</c>.</item>
/// </list>
/// </summary>
public sealed class DeletePrescriptionUseCaseTests : IDisposable
{
    private readonly string _workDirectory;

    public DeletePrescriptionUseCaseTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p2c4-delete-" + Guid.NewGuid().ToString("N"));
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

    private static DeletePrescriptionUseCase CreateUseCase(OpticDbContext context)
        => new(new PrescriptionRepository(context), new UnitOfWork(context));

    private static void EnsureSchema(string databasePath)
    {
        using var context = CreateContext(databasePath);
        context.Database.EnsureCreated();
    }

    private long SeedPrescription(string databasePath)
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

        var prescription = new Prescription
        {
            CustomerId = customer.CustomerId,
            IssueDate = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            DoctorName = "Dr House"
        };
        context.Prescriptions.Add(prescription);
        context.SaveChanges();
        return prescription.PrescriptionId;
    }

    // ------------------------------------------------------------------
    // (1) Suppression nominale : l'ordonnance existante est supprimée et persistée
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_DeletesExistingPrescription_AndPersists()
    {
        var dbPath = PathFor("delete.db");
        EnsureSchema(dbPath);
        var prescriptionId = SeedPrescription(dbPath);

        DeletePrescriptionResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            result = await useCase.ExecuteAsync(new DeletePrescriptionCommand { PrescriptionId = prescriptionId });
        }

        result.PrescriptionFound.Should().BeTrue();
        result.PrescriptionId.Should().Be(prescriptionId);

        using var verify = CreateContext(dbPath);
        verify.Prescriptions.AsNoTracking().Should().BeEmpty("l'ordonnance supprimée n'est plus persistée");
    }

    // ------------------------------------------------------------------
    // (2) Ordonnance introuvable → PrescriptionFound = false, aucune écriture
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_PrescriptionNotFound_ReturnsNotFound_AndDoesNotWrite()
    {
        var dbPath = PathFor("missing.db");
        EnsureSchema(dbPath);
        var survivorId = SeedPrescription(dbPath);

        DeletePrescriptionResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            result = await useCase.ExecuteAsync(new DeletePrescriptionCommand { PrescriptionId = 4242 });
        }

        result.PrescriptionFound.Should().BeFalse();
        result.PrescriptionId.Should().Be(4242);

        using var verify = CreateContext(dbPath);
        verify.Prescriptions.AsNoTracking().Should().ContainSingle(p => p.PrescriptionId == survivorId,
            "aucune ordonnance n'est supprimée quand l'id est introuvable");
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

        Action act = () => _ = new DeletePrescriptionUseCase(prescriptionRepository: null!, new UnitOfWork(context));
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithoutUnitOfWork_Throws()
    {
        var dbPath = PathFor("ctor-uow.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);

        Action act = () => _ = new DeletePrescriptionUseCase(new PrescriptionRepository(context), unitOfWork: null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
