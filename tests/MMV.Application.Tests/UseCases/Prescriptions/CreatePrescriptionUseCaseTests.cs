using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Prescriptions.CreatePrescription;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Domain.Interfaces.Repositories;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Repositories;
using Moq;
using Xunit;

namespace MMV.Application.Tests.UseCases.Prescriptions;

/// <summary>
/// P2C-4, sécurisé en <b>P3-3B</b> — Use case « Créer une ordonnance » (<see cref="CreatePrescriptionUseCase"/>).
///
/// Prouve, sur un <b>vrai SQLite temporaire</b> (jamais le provider InMemory) pour les écritures, et sur des
/// <b>mocks stricts</b> (<see cref="MockBehavior.Strict"/>) pour prouver l'<b>absence</b> d'écriture, que le use case :
/// <list type="number">
///   <item>crée une ordonnance et persiste (champs round-trip) ;</item>
///   <item>ne normalise pas les champs texte optionnels (comportement d'origine) ;</item>
///   <item><b>P3-3B</b> — commande invalide : <c>ValidationErrors</c>, <c>IsValid = false</c>, <b>aucun</b> accès au
///         dépôt (ni lecture du client, ni <c>CreateAsync</c>, ni <c>SaveChangesAsync</c>) ;</item>
///   <item><b>P3-3B</b> — client introuvable : <c>CustomerFound = false</c>, aucune écriture (la clé étrangère n'est
///         plus le chemin nominal) ;</item>
///   <item><b>P3-3B</b> — client archivé : <see cref="BusinessRuleException"/> au message stable, aucune écriture ;</item>
///   <item><b>P3-3B</b> — client actif : création acceptée ;</item>
///   <item><b>P3-3B</b> — axe <c>0</c> persisté à <c>180</c> (OD et OG) ; axe <c>180</c> inchangé ;</item>
///   <item><b>P3-3B</b> — <c>NaN</c> / infinis refusés sans écriture ;</item>
///   <item><b>P3-3B</b> — ordonnance partielle ou vide : acceptée ;</item>
///   <item>commande nulle : <see cref="ArgumentNullException"/> ; dépendances manquantes : constructeur rejette
///         <c>null</c> (y compris le nouvel <see cref="ICustomerRepository"/>).</item>
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
        => new(new PrescriptionRepository(context), new CustomerRepository(context), new UnitOfWork(context));

    private static void EnsureSchema(string databasePath)
    {
        using var context = CreateContext(databasePath);
        context.Database.EnsureCreated();
    }

    private static long SeedCustomer(string databasePath, bool archived = false)
    {
        using var context = CreateContext(databasePath);
        var customer = new Customer
        {
            FirstName = "Jean",
            LastName = "Dupont",
            CreatedAt = DateTime.UtcNow.AddDays(-10),
            UpdatedAt = DateTime.UtcNow.AddDays(-10)
        };

        if (archived)
            customer.Archive();

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
            result = await CreateUseCase(context).ExecuteAsync(SampleCommand(customerId));
        }

        result.PrescriptionId.Should().BeGreaterThan(0);
        result.CustomerFound.Should().BeTrue();
        result.IsValid.Should().BeTrue();

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
    // (2) Pas de normalisation des champs texte optionnels (comportement d'origine)
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
    // (3) P3-3B — commande invalide : aucune écriture, aucun accès au dépôt
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_InvalidCommand_ReturnsValidationErrors_AndTouchesNoRepository()
    {
        // Mocks STRICTS et SANS aucun setup : le moindre appel (lecture du client comprise) ferait échouer le test.
        // La validation précède délibérément le chargement du client.
        var prescriptions = new Mock<IPrescriptionRepository>(MockBehavior.Strict);
        var customers = new Mock<ICustomerRepository>(MockBehavior.Strict);
        var unitOfWork = new Mock<IUnitOfWork>(MockBehavior.Strict);

        var useCase = new CreatePrescriptionUseCase(prescriptions.Object, customers.Object, unitOfWork.Object);

        // Sphère hors plage ET axe orphelin : deux règles enfin réellement exécutées (le validateur était mort).
        var result = await useCase.ExecuteAsync(new CreatePrescriptionCommand
        {
            CustomerId = 1,
            IssueDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            OdSphere = 999,
            OgAxis = 90
        });

        result.IsValid.Should().BeFalse();
        result.ValidationErrors.Should().NotBeEmpty();
        result.PrescriptionId.Should().Be(0);

        customers.VerifyNoOtherCalls();
        prescriptions.VerifyNoOtherCalls();
        unitOfWork.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public async Task ExecuteAsync_NonFiniteValue_IsRejected_WithoutWriting(double value)
    {
        var prescriptions = new Mock<IPrescriptionRepository>(MockBehavior.Strict);
        var customers = new Mock<ICustomerRepository>(MockBehavior.Strict);
        var unitOfWork = new Mock<IUnitOfWork>(MockBehavior.Strict);

        var useCase = new CreatePrescriptionUseCase(prescriptions.Object, customers.Object, unitOfWork.Object);

        var result = await useCase.ExecuteAsync(new CreatePrescriptionCommand
        {
            CustomerId = 1,
            IssueDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            OdSphere = value
        });

        result.IsValid.Should().BeFalse();
        prescriptions.VerifyNoOtherCalls();
        unitOfWork.VerifyNoOtherCalls();
    }

    // ------------------------------------------------------------------
    // (4) P3-3B — client introuvable : CustomerFound = false, aucune écriture
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_CustomerNotFound_ReturnsNotFound_AndDoesNotWrite()
    {
        var prescriptions = new Mock<IPrescriptionRepository>(MockBehavior.Strict);
        var unitOfWork = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var customers = new Mock<ICustomerRepository>(MockBehavior.Strict);
        customers
            .Setup(r => r.GetByIdAsync(404L, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);

        var useCase = new CreatePrescriptionUseCase(prescriptions.Object, customers.Object, unitOfWork.Object);

        var result = await useCase.ExecuteAsync(SampleCommand(customerId: 404));

        result.CustomerFound.Should().BeFalse();
        result.PrescriptionId.Should().Be(0);
        result.IsValid.Should().BeTrue("la commande était valide : c'est le client qui n'existe pas");

        // Le refus est explicite : aucune DbUpdateException de clé étrangère n'est laissée devenir le chemin nominal.
        prescriptions.VerifyNoOtherCalls();
        unitOfWork.VerifyNoOtherCalls();
    }

    // ------------------------------------------------------------------
    // (5) P3-3B — client archivé : BusinessRuleException, aucune écriture
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_ArchivedCustomer_Throws_AndDoesNotWrite()
    {
        var archived = new Customer { CustomerId = 7, FirstName = "Jean", LastName = "Dupont" };
        archived.Archive();

        var prescriptions = new Mock<IPrescriptionRepository>(MockBehavior.Strict);
        var unitOfWork = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var customers = new Mock<ICustomerRepository>(MockBehavior.Strict);
        customers
            .Setup(r => r.GetByIdAsync(7L, It.IsAny<CancellationToken>()))
            .ReturnsAsync(archived);

        var useCase = new CreatePrescriptionUseCase(prescriptions.Object, customers.Object, unitOfWork.Object);

        Func<Task> act = () => useCase.ExecuteAsync(SampleCommand(customerId: 7));

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .WithMessage(CreatePrescriptionUseCase.CustomerArchivedMessage);

        prescriptions.VerifyNoOtherCalls();
        unitOfWork.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteAsync_ArchivedCustomer_PersistsNothing_OnRealDatabase()
    {
        var dbPath = PathFor("archived.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath, archived: true);

        using (var context = CreateContext(dbPath))
        {
            Func<Task> act = () => CreateUseCase(context).ExecuteAsync(SampleCommand(customerId));
            await act.Should().ThrowAsync<BusinessRuleException>();
        }

        using var verify = CreateContext(dbPath);
        verify.Prescriptions.AsNoTracking().Should().BeEmpty("le refus métier ne doit rien écrire");
    }

    // ------------------------------------------------------------------
    // (6) P3-3B — normalisation d'axe : 0 → 180, 180 inchangé (OD et OG)
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(0, 180)]
    [InlineData(180, 180)]
    [InlineData(90, 90)]
    public async Task ExecuteAsync_NormalizesAxis_OnBothEyes(int submittedAxis, int expectedAxis)
    {
        var dbPath = PathFor($"axis-{submittedAxis}.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);

        var command = new CreatePrescriptionCommand
        {
            CustomerId = customerId,
            IssueDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            OdCylinder = -1.0,
            OdAxis = submittedAxis,
            OgCylinder = -0.75,
            OgAxis = submittedAxis
        };

        using (var context = CreateContext(dbPath))
        {
            await CreateUseCase(context).ExecuteAsync(command);
        }

        using var verify = CreateContext(dbPath);
        var stored = verify.Prescriptions.AsNoTracking().Single();
        stored.OdAxis.Should().Be(expectedAxis);
        stored.OgAxis.Should().Be(expectedAxis);
    }

    // ------------------------------------------------------------------
    // (7) P3-3B — ordonnance partielle ou vide : acceptée (iso-comportement)
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_PartialPrescription_IsAccepted()
    {
        var dbPath = PathFor("partial.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);

        var command = new CreatePrescriptionCommand
        {
            CustomerId = customerId,
            IssueDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            OdSphere = -1.25 // OG entièrement vide
        };

        CreatePrescriptionResult result;
        using (var context = CreateContext(dbPath))
        {
            result = await CreateUseCase(context).ExecuteAsync(command);
        }

        result.IsValid.Should().BeTrue();
        result.PrescriptionId.Should().BeGreaterThan(0);

        using var verify = CreateContext(dbPath);
        var stored = verify.Prescriptions.AsNoTracking().Single();
        stored.OdSphere.Should().Be(-1.25);
        stored.OgSphere.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_EmptyPrescription_IsAccepted()
    {
        var dbPath = PathFor("empty.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);

        var command = new CreatePrescriptionCommand
        {
            CustomerId = customerId,
            IssueDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc)
        };

        CreatePrescriptionResult result;
        using (var context = CreateContext(dbPath))
        {
            result = await CreateUseCase(context).ExecuteAsync(command);
        }

        // Aucune règle « au moins une valeur optique » n'est ajoutée en P3-3B (décision P3-3A, iso-comportement).
        result.IsValid.Should().BeTrue();
        result.PrescriptionId.Should().BeGreaterThan(0);
    }

    // ------------------------------------------------------------------
    // (8) Commande nulle → ArgumentNullException
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
    // (9) Dépendances critiques manquantes → le constructeur rejette null
    // ------------------------------------------------------------------

    [Fact]
    public void Constructor_WithoutRepository_Throws()
    {
        var dbPath = PathFor("ctor-repo.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);

        Action act = () => _ = new CreatePrescriptionUseCase(
            prescriptionRepository: null!, new CustomerRepository(context), new UnitOfWork(context));
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithoutCustomerRepository_Throws()
    {
        var dbPath = PathFor("ctor-customer-repo.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);

        Action act = () => _ = new CreatePrescriptionUseCase(
            new PrescriptionRepository(context), customerRepository: null!, new UnitOfWork(context));
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithoutUnitOfWork_Throws()
    {
        var dbPath = PathFor("ctor-uow.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);

        Action act = () => _ = new CreatePrescriptionUseCase(
            new PrescriptionRepository(context), new CustomerRepository(context), unitOfWork: null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
