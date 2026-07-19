using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.WorkshopSheets.GenerateWorkshopSheet;
using MMV.Application.UseCases.WorkshopSheets.GetCurrentWorkshopSheet;
using MMV.Application.UseCases.WorkshopSheets.ListWorkshopSheetVersions;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Domain.Services;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.WorkshopSheets;

/// <summary>
/// P3-6B — Génération, versionnement et lecture d'une fiche atelier, sur <b>vrai SQLite</b>.
///
/// Prouve : snapshot complet et minimal, transposition stockée sans toucher la source, refus métier par statut
/// et par absence de ligne, versionnement (v1 courante, v2 rend v1 non courante, QC non reporté), détection
/// d'obsolescence, et arbitrage de la base en génération concurrente.
/// </summary>
public sealed class GenerateWorkshopSheetUseCaseTests : WorkshopSheetTestBase
{
    private static GenerateWorkshopSheetUseCase CreateUseCase(OpticDbContext context)
        => new(new OrderRepository(context), new EfTransactionRunner(context));

    private static async Task<GenerateWorkshopSheetResult> GenerateAsync(string dbPath, long orderId)
    {
        using var context = CreateContext(dbPath);
        return await CreateUseCase(context).ExecuteAsync(new GenerateWorkshopSheetCommand { OrderId = orderId });
    }

    // -------------------------------------------------------------------------------------------------------
    // Génération nominale et snapshot
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Generate_PremiereVersion_EstNumeroUnEtCourante()
    {
        var dbPath = PathFor("v1.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.InProgress);

        var result = await GenerateAsync(dbPath, orderId);

        result.OrderFound.Should().BeTrue();
        result.Sheet.Should().NotBeNull();
        result.Sheet!.Version.Should().Be(1);
        result.Sheet.IsCurrent.Should().BeTrue();
        result.Sheet.QcStatus.Should().Be(WorkshopSheetQcStatus.Pending);
        result.Sheet.IsUpToDate.Should().BeTrue();

        using var context = CreateContext(dbPath);
        context.WorkshopSheets.Count(w => w.OrderId == orderId).Should().Be(1);
    }

    [Fact]
    public async Task Generate_CopieToutesLesLignesDeLaCommande()
    {
        var dbPath = PathFor("lines.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.InProgress);

        var result = await GenerateAsync(dbPath, orderId);

        result.Sheet!.Items.Should().HaveCount(2);
        result.Sheet.Items.Select(i => i.ItemType)
            .Should().BeEquivalentTo(new[] { OrderItemType.Frame, OrderItemType.LensOd });
    }

    [Fact]
    public async Task Generate_CopiePrismeUsageEtAcuite_QueLeDtoDeLectureActuelPerd()
    {
        // Constat de l'audit P3-6B : OrderDetailsItemDto ne transporte que Sphere/Cylinder/Axis/Addition.
        // Le snapshot DOIT être construit depuis OrderItem, sinon le bon d'atelier serait amputé.
        var dbPath = PathFor("optics.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.InProgress);

        var result = await GenerateAsync(dbPath, orderId);

        var lens = result.Sheet!.Items.Single(i => i.ItemType == OrderItemType.LensOd);
        lens.UsageType.Should().Be(LensUsageType.Progressive);
        lens.PrismValue.Should().Be(1.50);
        lens.PrismBase.Should().Be(PrismBase.Out);
        lens.VisualAcuity.Should().Be("10/10");
        lens.Addition.Should().Be(2.50);
    }

    [Fact]
    public async Task Generate_StockeLaNotationTransposee_SansModifierLaSource()
    {
        var dbPath = PathFor("transpose.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.InProgress);

        var result = await GenerateAsync(dbPath, orderId);

        var lens = result.Sheet!.Items.Single(i => i.ItemType == OrderItemType.LensOd);
        lens.SourceSphere.Should().Be(2.00);
        lens.SourceCylinder.Should().Be(-1.00);
        lens.SourceAxis.Should().Be(180);
        lens.HasTransposition.Should().BeTrue();
        lens.TransposedSphere.Should().Be(1.00);
        lens.TransposedCylinder.Should().Be(1.00);
        lens.TransposedAxis.Should().Be(90);

        // La ligne de commande d'origine est INTACTE en base.
        using var context = CreateContext(dbPath);
        var sourceItem = context.OrderItems.Single(i => i.OrderId == orderId && i.ItemType == OrderItemType.LensOd);
        sourceItem.Sphere.Should().Be(2.00);
        sourceItem.Cylinder.Should().Be(-1.00);
        sourceItem.Axis.Should().Be(180);
        sourceItem.Addition.Should().Be(2.50);
        sourceItem.PrismValue.Should().Be(1.50);
    }

    [Fact]
    public async Task Generate_NeCopieQueLeNomDuClient()
    {
        var dbPath = PathFor("minimal.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.InProgress);

        var result = await GenerateAsync(dbPath, orderId);

        result.Sheet!.CustomerName.Should().Be("Jean Dupont");

        // Ni le DTO ni le modèle persisté ne comportent de champ téléphone / email / montant.
        var dtoProperties = typeof(MMV.Application.UseCases.WorkshopSheets.WorkshopSheetDto)
            .GetProperties().Select(p => p.Name).ToArray();
        dtoProperties.Should().NotContain(n => n.Contains("Phone", StringComparison.OrdinalIgnoreCase));
        dtoProperties.Should().NotContain(n => n.Contains("Email", StringComparison.OrdinalIgnoreCase));
        dtoProperties.Should().NotContain(n => n.Contains("Amount", StringComparison.OrdinalIgnoreCase));
    }

    // -------------------------------------------------------------------------------------------------------
    // Refus métier
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Generate_CommandeIntrouvable_RenvoieOrderFoundFalse_SansEcriture()
    {
        var dbPath = PathFor("notfound.db");
        EnsureSchema(dbPath);

        var result = await GenerateAsync(dbPath, orderId: 12345);

        result.OrderFound.Should().BeFalse();
        result.Sheet.Should().BeNull();

        using var context = CreateContext(dbPath);
        context.WorkshopSheets.Should().BeEmpty();
    }

    [Theory]
    [InlineData(OrderStatus.New)]
    [InlineData(OrderStatus.ToFabricate)]
    [InlineData(OrderStatus.Ready)]
    [InlineData(OrderStatus.Delivered)]
    public async Task Generate_StatutHorsAtelier_EstRefuse_SansEcriture(OrderStatus status)
    {
        var dbPath = PathFor($"status-{status}.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, status);

        var act = async () => await GenerateAsync(dbPath, orderId);

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .WithMessage(WorkshopSheetPolicy.GenerationStatusMessage);

        using var context = CreateContext(dbPath);
        context.WorkshopSheets.Should().BeEmpty();
    }

    [Theory]
    [InlineData(OrderStatus.InProgress)]
    [InlineData(OrderStatus.QualityCheck)]
    public async Task Generate_StatutDAtelier_EstAutorise(OrderStatus status)
    {
        var dbPath = PathFor($"ok-{status}.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, status);

        var result = await GenerateAsync(dbPath, orderId);

        result.Sheet.Should().NotBeNull();
    }

    [Fact]
    public async Task Generate_CommandeSansLigne_EstRefusee_SansEcriture()
    {
        var dbPath = PathFor("noitems.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrderWithoutItems(dbPath, OrderStatus.InProgress);

        var act = async () => await GenerateAsync(dbPath, orderId);

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .WithMessage(WorkshopSheetPolicy.GenerationWithoutItemsMessage);

        using var context = CreateContext(dbPath);
        context.WorkshopSheets.Should().BeEmpty();
    }

    [Fact]
    public async Task Generate_CommandeNulle_LeveArgumentNullException()
    {
        var dbPath = PathFor("nullcmd.db");
        EnsureSchema(dbPath);

        using var context = CreateContext(dbPath);
        var useCase = CreateUseCase(context);

        var act = async () => await useCase.ExecuteAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // -------------------------------------------------------------------------------------------------------
    // Versionnement
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Generate_DeuxiemeVersion_RendLaPremiereNonCourante()
    {
        var dbPath = PathFor("v2.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.InProgress);

        await GenerateAsync(dbPath, orderId);
        var second = await GenerateAsync(dbPath, orderId);

        second.Sheet!.Version.Should().Be(2);
        second.Sheet.IsCurrent.Should().BeTrue();
        second.Sheet.QcStatus.Should().Be(WorkshopSheetQcStatus.Pending);

        using var context = CreateContext(dbPath);
        var sheets = context.WorkshopSheets.Where(w => w.OrderId == orderId).OrderBy(w => w.Version).ToList();
        sheets.Should().HaveCount(2);
        sheets[0].Version.Should().Be(1);
        sheets[0].IsCurrent.Should().BeFalse();
        sheets[1].Version.Should().Be(2);
        sheets[1].IsCurrent.Should().BeTrue();
    }

    [Fact]
    public async Task Generate_ApresRefusQc_CreeUneVersionDeRepriseEnAttente()
    {
        var dbPath = PathFor("afterfail.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.QualityCheck);

        var first = await GenerateAsync(dbPath, orderId);

        // Refus du contrôle sur la v1.
        using (var context = CreateContext(dbPath))
        {
            var repository = new OrderRepository(context);
            await repository.TryTakeWorkshopSheetQcDecisionAsync(
                first.Sheet!.WorkshopSheetId, WorkshopSheetQcStatus.Failed, "Rayure sur le verre", DateTime.UtcNow);
        }

        var second = await GenerateAsync(dbPath, orderId);

        // La reprise repart à zéro : le résultat QC n'est JAMAIS reporté.
        second.Sheet!.Version.Should().Be(2);
        second.Sheet.QcStatus.Should().Be(WorkshopSheetQcStatus.Pending);
        second.Sheet.QcComment.Should().BeNull();

        // La version refusée reste historique et immuable.
        using var verifyContext = CreateContext(dbPath);
        var v1 = verifyContext.WorkshopSheets.Single(w => w.OrderId == orderId && w.Version == 1);
        v1.QcStatus.Should().Be(WorkshopSheetQcStatus.Failed);
        v1.QcComment.Should().Be("Rayure sur le verre");
        v1.IsCurrent.Should().BeFalse();
    }

    [Fact]
    public async Task Generate_UneSeuleVersionCourantePeutExister()
    {
        var dbPath = PathFor("single-current.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.InProgress);

        await GenerateAsync(dbPath, orderId);
        await GenerateAsync(dbPath, orderId);
        await GenerateAsync(dbPath, orderId);

        using var context = CreateContext(dbPath);
        context.WorkshopSheets.Count(w => w.OrderId == orderId && w.IsCurrent).Should().Be(1);
    }

    [Fact]
    public async Task Base_RefuseDeuxVersionsCourantes_ParIndexUniqueFiltre()
    {
        var dbPath = PathFor("unique-current.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.InProgress);
        await GenerateAsync(dbPath, orderId);

        // Tentative d'insertion directe d'une SECONDE version courante : la base doit refuser.
        using var context = CreateContext(dbPath);
        context.WorkshopSheets.Add(new WorkshopSheet
        {
            OrderId = orderId,
            Version = 99,
            IsCurrent = true,
            TechnicalFingerprint = new string('a', 64),
            OrderNumberSnapshot = "CMD-000100",
            OrderDateSnapshot = DateTime.UtcNow,
            CustomerNameSnapshot = "Jean Dupont",
            QcStatus = WorkshopSheetQcStatus.Pending
        });

        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Base_RefuseDeuxFoisLeMemeCoupleOrderIdVersion()
    {
        var dbPath = PathFor("unique-version.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.InProgress);
        await GenerateAsync(dbPath, orderId);

        using var context = CreateContext(dbPath);
        context.WorkshopSheets.Add(new WorkshopSheet
        {
            OrderId = orderId,
            Version = 1,               // doublon du couple (OrderId, Version)
            IsCurrent = false,         // non courante : seul le couple est en cause
            TechnicalFingerprint = new string('b', 64),
            OrderNumberSnapshot = "CMD-000100",
            OrderDateSnapshot = DateTime.UtcNow,
            CustomerNameSnapshot = "Jean Dupont",
            QcStatus = WorkshopSheetQcStatus.Pending
        });

        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Generate_Concurrente_PreserveLesInvariantsEtNExposeAucuneErreurTechnique()
    {
        // NB — SQLite sérialise les écritures (écrivain unique) : on ne peut pas forcer de façon déterministe
        // laquelle des deux générations perdra la course. Ce test assère donc l'INVARIANT DE SÛRETÉ, comme le
        // fait déjà le test de concurrence de P3-5 : quel que soit l'entrelacement obtenu, la base ne doit
        // jamais laisser deux versions courantes ni deux fois le même numéro de version, et un éventuel échec
        // doit être une exception MÉTIER contrôlée — jamais une DbUpdateException/SqliteException brute.
        var dbPath = PathFor("concurrent.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.InProgress);

        async Task<Exception?> TryGenerateAsync()
        {
            try
            {
                await GenerateAsync(dbPath, orderId);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        var failures = (await Task.WhenAll(TryGenerateAsync(), TryGenerateAsync()))
            .Where(e => e is not null)
            .ToList();

        // Aucune fuite d'erreur technique : seuls des refus métier contrôlés peuvent remonter. Formulé en
        // « ne doit contenir aucun autre type » afin de rester valide quand les deux générations réussissent
        // (entrelacement sérialisé : v1 puis v2), qui est un résultat parfaitement légitime.
        failures
            .Where(e => e is not WorkshopSheetVersionConflictException && e is not PersistenceException)
            .Should().BeEmpty("seuls des refus métier contrôlés peuvent remonter d'une génération concurrente");

        using var context = CreateContext(dbPath);
        var sheets = context.WorkshopSheets.Where(w => w.OrderId == orderId).ToList();

        sheets.Should().NotBeEmpty();
        sheets.Count(w => w.IsCurrent).Should().Be(1, "au plus une version courante par commande");
        sheets.Select(w => w.Version).Should().OnlyHaveUniqueItems("le couple (OrderId, Version) est unique");
    }

    // -------------------------------------------------------------------------------------------------------
    // Lecture : version courante, historique, obsolescence
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task GetCurrent_SansFiche_RenvoieSheetFoundFalse()
    {
        var dbPath = PathFor("get-none.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.InProgress);

        using var context = CreateContext(dbPath);
        var result = await new GetCurrentWorkshopSheetUseCase(new OrderRepository(context))
            .ExecuteAsync(new GetCurrentWorkshopSheetQuery { OrderId = orderId });

        result.OrderFound.Should().BeTrue();
        result.SheetFound.Should().BeFalse();
        result.Sheet.Should().BeNull();
    }

    [Fact]
    public async Task GetCurrent_RenvoieLaFicheAvecSesLignesOrdonnees()
    {
        var dbPath = PathFor("get-current.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.InProgress);
        await GenerateAsync(dbPath, orderId);

        using var context = CreateContext(dbPath);
        var result = await new GetCurrentWorkshopSheetUseCase(new OrderRepository(context))
            .ExecuteAsync(new GetCurrentWorkshopSheetQuery { OrderId = orderId });

        result.SheetFound.Should().BeTrue();
        result.Sheet!.Items.Should().HaveCount(2);
        result.Sheet.Items.Select(i => i.Position).Should().BeInAscendingOrder();
        result.Sheet.IsUpToDate.Should().BeTrue();
    }

    [Fact]
    public async Task GetCurrent_ApresChangementTechnique_SignaleLaFicheObsolete()
    {
        var dbPath = PathFor("obsolete.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.InProgress);
        await GenerateAsync(dbPath, orderId);

        ChangeOrderTechnicalData(dbPath, orderId);

        using var context = CreateContext(dbPath);
        var result = await new GetCurrentWorkshopSheetUseCase(new OrderRepository(context))
            .ExecuteAsync(new GetCurrentWorkshopSheetQuery { OrderId = orderId });

        result.Sheet!.IsUpToDate.Should().BeFalse();
    }

    [Fact]
    public async Task GetCurrent_LeSnapshotNeSuitPasLesChangementsDeCatalogueNiDeClient()
    {
        var dbPath = PathFor("frozen.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.InProgress);
        await GenerateAsync(dbPath, orderId);

        // Le catalogue et le client changent APRÈS la génération.
        using (var context = CreateContext(dbPath))
        {
            var product = context.Products.Single(p => p.Reference == "MON-001");
            product.Name = "Monture RENOMMÉE";
            product.IsActive = false;

            var customer = context.Customers.Single();
            customer.LastName = "MARIÉE";

            context.SaveChanges();
        }

        using var verifyContext = CreateContext(dbPath);
        var result = await new GetCurrentWorkshopSheetUseCase(new OrderRepository(verifyContext))
            .ExecuteAsync(new GetCurrentWorkshopSheetQuery { OrderId = orderId });

        // La fiche est un document historique : elle affiche ce qu'elle affichait le jour de sa création.
        result.Sheet!.CustomerName.Should().Be("Jean Dupont");
        result.Sheet.Items.Single(i => i.ItemType == OrderItemType.Frame).ProductName.Should().Be("Monture Alpha");
    }

    [Fact]
    public async Task ListVersions_RenvoieLHistoriqueEnVersionDecroissante()
    {
        var dbPath = PathFor("history.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.InProgress);
        await GenerateAsync(dbPath, orderId);
        await GenerateAsync(dbPath, orderId);

        using var context = CreateContext(dbPath);
        var result = await new ListWorkshopSheetVersionsUseCase(new OrderRepository(context))
            .ExecuteAsync(new ListWorkshopSheetVersionsQuery { OrderId = orderId });

        result.OrderFound.Should().BeTrue();
        result.Versions.Select(v => v.Version).Should().ContainInOrder(2, 1);
        result.Versions.Count(v => v.IsCurrent).Should().Be(1);
    }

    [Fact]
    public async Task ListVersions_CommandeIntrouvable_RenvoieOrderFoundFalse()
    {
        var dbPath = PathFor("history-none.db");
        EnsureSchema(dbPath);

        using var context = CreateContext(dbPath);
        var result = await new ListWorkshopSheetVersionsUseCase(new OrderRepository(context))
            .ExecuteAsync(new ListWorkshopSheetVersionsQuery { OrderId = 999 });

        result.OrderFound.Should().BeFalse();
        result.Versions.Should().BeEmpty();
    }
}
