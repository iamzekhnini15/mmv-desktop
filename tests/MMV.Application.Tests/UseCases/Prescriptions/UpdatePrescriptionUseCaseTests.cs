using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Prescriptions.UpdatePrescription;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Prescriptions;

/// <summary>
/// P2C-4 — Use case « Modifier une ordonnance » (<see cref="UpdatePrescriptionUseCase"/>). P2C-4 introduit la vraie
/// mise à jour : le formulaire d'origine, en « édition », appelait toujours <c>CreateAsync</c> sans capturer
/// l'identifiant (il créait un doublon). La ViewModel capture désormais <c>PrescriptionId</c> et délègue ici.
///
/// Prouve, sur un <b>vrai SQLite temporaire</b> (jamais le provider InMemory), que le use case :
/// <list type="number">
///   <item>met à jour une ordonnance existante et persiste (PrescriptionFound = true) ;</item>
///   <item>ordonnance introuvable : renvoie <c>PrescriptionFound = false</c> sans écrire ;</item>
///   <item>préserve <c>CustomerId</c> et <c>CreatedAt</c> ;</item>
///   <item>commande nulle : <see cref="ArgumentNullException"/> ;</item>
///   <item>dépendances critiques manquantes : le constructeur rejette <c>null</c>.</item>
/// </list>
/// </summary>
public sealed class UpdatePrescriptionUseCaseTests : IDisposable
{
    private readonly string _workDirectory;

    public UpdatePrescriptionUseCaseTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p2c4-update-" + Guid.NewGuid().ToString("N"));
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

    private static UpdatePrescriptionUseCase CreateUseCase(OpticDbContext context)
        => new(new PrescriptionRepository(context), new UnitOfWork(context));

    private static void EnsureSchema(string databasePath)
    {
        using var context = CreateContext(databasePath);
        context.Database.EnsureCreated();
    }

    private static long SeedCustomer(OpticDbContext context)
    {
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

    /// <summary>Sème un client + une ordonnance, renvoie (prescriptionId, customerId, createdAt).</summary>
    private (long prescriptionId, long customerId, DateTime createdAt) SeedPrescription(string databasePath)
    {
        using var context = CreateContext(databasePath);
        var customerId = SeedCustomer(context);
        var createdAt = new DateTime(2025, 6, 1, 8, 0, 0, DateTimeKind.Utc);
        var prescription = new Prescription
        {
            CustomerId = customerId,
            IssueDate = new DateOnly(2025, 6, 1),
            DoctorName = "Dr Origine",
            OdSphere = -1.0,
            Notes = "avant",
            CreatedAt = createdAt
        };
        context.Prescriptions.Add(prescription);
        context.SaveChanges();
        return (prescription.PrescriptionId, customerId, createdAt);
    }

    // ------------------------------------------------------------------
    // (1) Mise à jour nominale : ordonnance existante modifiée et persistée
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_UpdatesExistingPrescription_AndPersists()
    {
        var dbPath = PathFor("update.db");
        EnsureSchema(dbPath);
        var (prescriptionId, _, _) = SeedPrescription(dbPath);

        UpdatePrescriptionResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            result = await useCase.ExecuteAsync(new UpdatePrescriptionCommand
            {
                PrescriptionId = prescriptionId,
                IssueDate = new DateOnly(2026, 3, 10),
                DoctorName = "Dr Modifié",
                OdSphere = -2.5,
                // AMENDÉ (P3-3B) : la fixture portait une base prismatique SANS valeur de prisme — la combinaison
                // « base seule » est désormais incohérente (le prisme n'existe que si sa valeur est positive). La
                // valeur manquante est ajoutée ; le cas « base seule » est prouvé refusé plus bas.
                OdPrismValue = 2.0,
                OdPrismBase = PrismBase.Up,
                Notes = "après"
            });
        }

        result.PrescriptionFound.Should().BeTrue();
        result.PrescriptionId.Should().Be(prescriptionId);
        result.IsValid.Should().BeTrue();

        using var verify = CreateContext(dbPath);
        verify.Prescriptions.AsNoTracking().Should().ContainSingle();
        var stored = verify.Prescriptions.AsNoTracking().Single();
        stored.DoctorName.Should().Be("Dr Modifié");
        stored.OdSphere.Should().Be(-2.5);
        stored.OdPrismValue.Should().Be(2.0);
        stored.OdPrismBase.Should().Be(PrismBase.Up);
        stored.Notes.Should().Be("après");
        stored.IssueDate.Should().Be(new DateOnly(2026, 3, 10));
    }

    // ------------------------------------------------------------------
    // (2) Ordonnance introuvable → PrescriptionFound = false, aucune écriture
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_PrescriptionNotFound_ReturnsNotFound_AndDoesNotWrite()
    {
        var dbPath = PathFor("missing.db");
        EnsureSchema(dbPath);
        var (survivorId, _, _) = SeedPrescription(dbPath);

        UpdatePrescriptionResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            result = await useCase.ExecuteAsync(new UpdatePrescriptionCommand
            {
                PrescriptionId = 4242,
                DoctorName = "Fantôme"
            });
        }

        result.PrescriptionFound.Should().BeFalse();
        result.PrescriptionId.Should().Be(4242);

        using var verify = CreateContext(dbPath);
        var survivor = verify.Prescriptions.AsNoTracking().Single(p => p.PrescriptionId == survivorId);
        survivor.DoctorName.Should().Be("Dr Origine", "aucune écriture ne doit avoir lieu si l'id est introuvable");
    }

    // ------------------------------------------------------------------
    // (3) Préserve CustomerId et CreatedAt
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_PreservesCustomerIdAndCreatedAt()
    {
        var dbPath = PathFor("preserve.db");
        EnsureSchema(dbPath);
        var (prescriptionId, customerId, createdAt) = SeedPrescription(dbPath);

        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            await useCase.ExecuteAsync(new UpdatePrescriptionCommand
            {
                PrescriptionId = prescriptionId,
                IssueDate = new DateOnly(2026, 3, 10),
                DoctorName = "Dr Modifié",
                Notes = "après"
            });
        }

        using var verify = CreateContext(dbPath);
        var stored = verify.Prescriptions.AsNoTracking().Single();
        stored.CustomerId.Should().Be(customerId, "une mise à jour ne réaffecte pas le propriétaire");
        stored.CreatedAt.Should().Be(createdAt, "la date de création est préservée");
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
    // (5) Dépendances critiques manquantes → le constructeur rejette null
    // ------------------------------------------------------------------

    [Fact]
    public void Constructor_WithoutRepository_Throws()
    {
        var dbPath = PathFor("ctor-repo.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);

        Action act = () => _ = new UpdatePrescriptionUseCase(prescriptionRepository: null!, new UnitOfWork(context));
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithoutUnitOfWork_Throws()
    {
        var dbPath = PathFor("ctor-uow.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);

        Action act = () => _ = new UpdatePrescriptionUseCase(new PrescriptionRepository(context), unitOfWork: null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ------------------------------------------------------------------
    // (6) P3-3B — commande invalide : aucune écriture, entité existante intacte
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_InvalidCommand_ReturnsValidationErrors_AndLeavesEntityUntouched()
    {
        var dbPath = PathFor("invalid.db");
        EnsureSchema(dbPath);
        var (prescriptionId, _, _) = SeedPrescription(dbPath);

        UpdatePrescriptionResult result;
        using (var context = CreateContext(dbPath))
        {
            result = await CreateUseCase(context).ExecuteAsync(new UpdatePrescriptionCommand
            {
                PrescriptionId = prescriptionId,
                IssueDate = new DateOnly(2026, 3, 10),
                DoctorName = "Dr Modifié",
                OdSphere = 999,          // hors plage
                OgPrismBase = PrismBase.In // base sans valeur de prisme
            });
        }

        result.PrescriptionFound.Should().BeTrue();
        result.IsValid.Should().BeFalse();
        result.ValidationErrors.Should().NotBeEmpty();

        // La validation opère sur un candidat séparé : l'entité suivie n'est pas même partiellement mutée.
        using var verify = CreateContext(dbPath);
        var stored = verify.Prescriptions.AsNoTracking().Single();
        stored.DoctorName.Should().Be("Dr Origine", "une commande invalide ne doit rien écrire");
        stored.OdSphere.Should().Be(-1.0);
        stored.Notes.Should().Be("avant");
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public async Task ExecuteAsync_NonFiniteValue_IsRejected_WithoutWriting(double value)
    {
        var dbPath = PathFor($"nonfinite-{value}.db");
        EnsureSchema(dbPath);
        var (prescriptionId, _, _) = SeedPrescription(dbPath);

        UpdatePrescriptionResult result;
        using (var context = CreateContext(dbPath))
        {
            result = await CreateUseCase(context).ExecuteAsync(new UpdatePrescriptionCommand
            {
                PrescriptionId = prescriptionId,
                IssueDate = new DateOnly(2026, 3, 10),
                OdSphere = value
            });
        }

        result.IsValid.Should().BeFalse();

        using var verify = CreateContext(dbPath);
        verify.Prescriptions.AsNoTracking().Single().OdSphere.Should().Be(-1.0);
    }

    // ------------------------------------------------------------------
    // (7) P3-3B — normalisation d'axe (0 → 180) à la mise à jour
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_NormalizesZeroAxis_ToOneHundredEighty()
    {
        var dbPath = PathFor("update-axis.db");
        EnsureSchema(dbPath);
        var (prescriptionId, _, _) = SeedPrescription(dbPath);

        using (var context = CreateContext(dbPath))
        {
            await CreateUseCase(context).ExecuteAsync(new UpdatePrescriptionCommand
            {
                PrescriptionId = prescriptionId,
                IssueDate = new DateOnly(2026, 3, 10),
                OdCylinder = -1.0,
                OdAxis = 0,
                OgCylinder = -0.75,
                OgAxis = 180
            });
        }

        using var verify = CreateContext(dbPath);
        var stored = verify.Prescriptions.AsNoTracking().Single();
        stored.OdAxis.Should().Be(180, "l'axe 0 est accepté en saisie puis persisté sous sa forme canonique");
        stored.OgAxis.Should().Be(180);
    }

    // ------------------------------------------------------------------
    // (8) P3-3B — client archivé : la CORRECTION reste autorisée
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_ArchivedCustomer_StillAllowsCorrection()
    {
        // Politique A (P3-3A) : « archivé » = plus de NOUVELLE activité, pas « données gelées ». Corriger une
        // ordonnance mal saisie ne doit pas obliger à réactiver puis ré-archiver le client. Ce test rend un
        // durcissement futur conscient plutôt qu'accidentel.
        var dbPath = PathFor("archived-update.db");
        EnsureSchema(dbPath);
        var (prescriptionId, customerId, _) = SeedPrescription(dbPath);

        using (var context = CreateContext(dbPath))
        {
            var customer = context.Customers.Single(c => c.CustomerId == customerId);
            customer.Archive();
            context.SaveChanges();
        }

        UpdatePrescriptionResult result;
        using (var context = CreateContext(dbPath))
        {
            result = await CreateUseCase(context).ExecuteAsync(new UpdatePrescriptionCommand
            {
                PrescriptionId = prescriptionId,
                IssueDate = new DateOnly(2026, 3, 10),
                DoctorName = "Dr Corrigé",
                OdSphere = -2.0
            });
        }

        result.PrescriptionFound.Should().BeTrue();
        result.IsValid.Should().BeTrue();

        using var verify = CreateContext(dbPath);
        var stored = verify.Prescriptions.AsNoTracking().Single();
        stored.DoctorName.Should().Be("Dr Corrigé");
        stored.OdSphere.Should().Be(-2.0);
    }

    // ------------------------------------------------------------------
    // (9) P3-3B — ordonnance partielle : acceptée
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_PartialPrescription_IsAccepted()
    {
        var dbPath = PathFor("update-partial.db");
        EnsureSchema(dbPath);
        var (prescriptionId, _, _) = SeedPrescription(dbPath);

        UpdatePrescriptionResult result;
        using (var context = CreateContext(dbPath))
        {
            result = await CreateUseCase(context).ExecuteAsync(new UpdatePrescriptionCommand
            {
                PrescriptionId = prescriptionId,
                IssueDate = new DateOnly(2026, 3, 10),
                OgSphere = -0.75 // OD entièrement vidé
            });
        }

        result.IsValid.Should().BeTrue();

        using var verify = CreateContext(dbPath);
        var stored = verify.Prescriptions.AsNoTracking().Single();
        stored.OgSphere.Should().Be(-0.75);
        stored.OdSphere.Should().BeNull();
    }
}
