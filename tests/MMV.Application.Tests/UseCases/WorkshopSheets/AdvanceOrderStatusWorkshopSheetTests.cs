using FluentAssertions;
using MMV.Application.UseCases.Orders.AdvanceOrderStatus;
using MMV.Application.UseCases.WorkshopSheets.GenerateWorkshopSheet;
using MMV.Application.UseCases.WorkshopSheets.ValidateWorkshopSheetQc;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Domain.Services;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.Repositories;
using MMV.Infrastructure.Services;
using Xunit;

namespace MMV.Application.Tests.UseCases.WorkshopSheets;

/// <summary>
/// P3-6B — Intégration de la fiche atelier dans le workflow de commande (<c>AdvanceOrderStatusUseCase</c>).
///
/// Prouve : génération automatique de la première fiche (et son idempotence), atomicité avec le stock,
/// impossibilité pour une commande neuve d'atteindre <c>QualityCheck</c> sans fiche, garde QC bloquant
/// <c>QualityCheck → Ready</c>, et compatibilité des commandes historiques sans fiche.
/// </summary>
public sealed class AdvanceOrderStatusWorkshopSheetTests : WorkshopSheetTestBase
{
    private static AdvanceOrderStatusUseCase CreateUseCase(OpticDbContext context)
        => new(
            new OrderRepository(context),
            new StockMovementRepository(context),
            new UnitOfWork(context),
            new EfTransactionRunner(context),
            new EfStockMutationService(context),
            SystemClock.Instance,
            new NotificationRepository(context));

    private static async Task<AdvanceOrderStatusResult> AdvanceAsync(
        string dbPath, long orderId, OrderStatus from, OrderStatus to)
    {
        using var context = CreateContext(dbPath);
        return await CreateUseCase(context).ExecuteAsync(new AdvanceOrderStatusCommand
        {
            OrderId = orderId,
            CurrentStatus = from,
            NextStatus = to,
            CustomerDisplayName = "Jean Dupont",
            CurrentStatusDisplay = from.ToString(),
            NextStatusDisplay = to.ToString()
        });
    }

    // -------------------------------------------------------------------------------------------------------
    // Génération automatique de la première fiche
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ToFabricate_Vers_InProgress_GenereAutomatiquementLaVersion1()
    {
        var dbPath = PathFor("auto-v1.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.ToFabricate);

        var result = await AdvanceAsync(dbPath, orderId, OrderStatus.ToFabricate, OrderStatus.InProgress);

        result.OrderFound.Should().BeTrue();
        result.HasCreatedWorkshopSheet.Should().BeTrue();

        using var context = CreateContext(dbPath);
        var sheets = context.WorkshopSheets.Where(w => w.OrderId == orderId).ToList();
        sheets.Should().HaveCount(1);
        sheets[0].Version.Should().Be(1);
        sheets[0].IsCurrent.Should().BeTrue();
        sheets[0].QcStatus.Should().Be(WorkshopSheetQcStatus.Pending);
        context.WorkshopSheetItems.Count(i => i.WorkshopSheetId == sheets[0].WorkshopSheetId).Should().Be(2);
    }

    [Fact]
    public async Task ToFabricate_Vers_InProgress_GenereLaFicheDansLaMemeTransactionQueLeStock()
    {
        var dbPath = PathFor("auto-stock.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.ToFabricate);

        var result = await AdvanceAsync(dbPath, orderId, OrderStatus.ToFabricate, OrderStatus.InProgress);

        result.HasCreatedStockMovements.Should().BeTrue();
        result.HasCreatedWorkshopSheet.Should().BeTrue();

        using var context = CreateContext(dbPath);
        // Décrément de stock ET fiche : les deux effets sont présents ensemble.
        context.StockMovements.Should().NotBeEmpty();
        context.WorkshopSheets.Count(w => w.OrderId == orderId).Should().Be(1);
    }

    [Fact]
    public async Task EchecDeStock_AnnuleAussiLaGenerationDeFiche()
    {
        // Preuve d'ATOMICITÉ : la fiche est créée dans la transaction de la transition. Si le stock est
        // insuffisant, le rollback doit également supprimer la fiche — pas de bon d'atelier orphelin pour une
        // fabrication qui n'a jamais démarré.
        var dbPath = PathFor("rollback.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.ToFabricate);

        using (var context = CreateContext(dbPath))
        {
            foreach (var product in context.Products)
            {
                product.StockQuantity = 0;
            }
            context.SaveChanges();
        }

        var act = async () => await AdvanceAsync(dbPath, orderId, OrderStatus.ToFabricate, OrderStatus.InProgress);

        await act.Should().ThrowAsync<InsufficientStockException>();

        using var verifyContext = CreateContext(dbPath);
        verifyContext.WorkshopSheets.Should().BeEmpty("le rollback annule la fiche comme tout le reste");
        verifyContext.WorkshopSheetItems.Should().BeEmpty();
        verifyContext.StockMovements.Should().BeEmpty();
        verifyContext.Orders.Single(o => o.OrderId == orderId).Status.Should().Be(OrderStatus.ToFabricate);
    }

    [Fact]
    public async Task InProgress_Vers_QualityCheck_NeCreePasUneSecondeFiche()
    {
        var dbPath = PathFor("no-duplicate.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.ToFabricate);

        await AdvanceAsync(dbPath, orderId, OrderStatus.ToFabricate, OrderStatus.InProgress);
        var second = await AdvanceAsync(dbPath, orderId, OrderStatus.InProgress, OrderStatus.QualityCheck);

        second.HasCreatedWorkshopSheet.Should().BeFalse("la commande possède déjà une fiche courante");

        using var context = CreateContext(dbPath);
        context.WorkshopSheets.Count(w => w.OrderId == orderId).Should().Be(1);
    }

    [Fact]
    public async Task InProgress_Vers_QualityCheck_GenereLaFichePourUneCommandeHistoriqueSansFiche()
    {
        // Filet de compatibilité : une commande déjà InProgress au moment du déploiement de P3-6B n'est jamais
        // passée par le point de génération nominal. Elle obtient sa fiche ici.
        var dbPath = PathFor("historic-inprogress.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.InProgress);

        var result = await AdvanceAsync(dbPath, orderId, OrderStatus.InProgress, OrderStatus.QualityCheck);

        result.HasCreatedWorkshopSheet.Should().BeTrue();

        using var context = CreateContext(dbPath);
        context.WorkshopSheets.Count(w => w.OrderId == orderId).Should().Be(1);
    }

    [Fact]
    public async Task UneCommandeCreeeApresP36B_NePeutPasAtteindreQualityCheckSansFiche()
    {
        // Exigence centrale de P3-6B : le parcours complet garantit qu'une fiche existe AVANT le contrôle qualité.
        var dbPath = PathFor("no-bypass.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.New);

        await AdvanceAsync(dbPath, orderId, OrderStatus.New, OrderStatus.ToFabricate);

        using (var afterFirst = CreateContext(dbPath))
        {
            afterFirst.WorkshopSheets.Should().BeEmpty("aucune fiche n'est créée avant l'entrée en fabrication");
        }

        await AdvanceAsync(dbPath, orderId, OrderStatus.ToFabricate, OrderStatus.InProgress);
        await AdvanceAsync(dbPath, orderId, OrderStatus.InProgress, OrderStatus.QualityCheck);

        using var context = CreateContext(dbPath);
        context.Orders.Single(o => o.OrderId == orderId).Status.Should().Be(OrderStatus.QualityCheck);
        context.WorkshopSheets.Count(w => w.OrderId == orderId).Should()
            .Be(1, "toute commande atteignant QualityCheck possède nécessairement une fiche");
    }

    // -------------------------------------------------------------------------------------------------------
    // Garde QC sur QualityCheck → Ready
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task QualityCheck_Vers_Ready_AvecFichePending_EstRefuse_SansEcriture()
    {
        var dbPath = PathFor("ready-pending.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.ToFabricate);
        await AdvanceAsync(dbPath, orderId, OrderStatus.ToFabricate, OrderStatus.InProgress);
        await AdvanceAsync(dbPath, orderId, OrderStatus.InProgress, OrderStatus.QualityCheck);

        var notificationsBefore = CountNotifications(dbPath);

        var act = async () => await AdvanceAsync(dbPath, orderId, OrderStatus.QualityCheck, OrderStatus.Ready);

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .WithMessage(WorkshopSheetPolicy.ReadyRequiresPassedQcMessage);

        using var context = CreateContext(dbPath);
        context.Orders.Single(o => o.OrderId == orderId).Status.Should().Be(OrderStatus.QualityCheck);
        CountNotifications(dbPath).Should().Be(notificationsBefore, "aucune écriture après un refus métier");
    }

    [Fact]
    public async Task QualityCheck_Vers_Ready_AvecFicheFailed_EstRefuse()
    {
        var dbPath = PathFor("ready-failed.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.ToFabricate);
        await AdvanceAsync(dbPath, orderId, OrderStatus.ToFabricate, OrderStatus.InProgress);
        await AdvanceAsync(dbPath, orderId, OrderStatus.InProgress, OrderStatus.QualityCheck);

        var sheetId = GetCurrentSheetId(dbPath, orderId);
        using (var context = CreateContext(dbPath))
        {
            await new OrderRepository(context).TryTakeWorkshopSheetQcDecisionAsync(
                sheetId, WorkshopSheetQcStatus.Failed, "Défaut", DateTime.UtcNow);
        }

        var act = async () => await AdvanceAsync(dbPath, orderId, OrderStatus.QualityCheck, OrderStatus.Ready);

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .WithMessage(WorkshopSheetPolicy.ReadyRequiresPassedQcMessage);

        using var verifyContext = CreateContext(dbPath);
        verifyContext.Orders.Single(o => o.OrderId == orderId).Status.Should().Be(OrderStatus.QualityCheck);
    }

    [Fact]
    public async Task QualityCheck_Vers_Ready_AvecFichePassed_EstAutorise()
    {
        var dbPath = PathFor("ready-passed.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.ToFabricate);
        await AdvanceAsync(dbPath, orderId, OrderStatus.ToFabricate, OrderStatus.InProgress);
        await AdvanceAsync(dbPath, orderId, OrderStatus.InProgress, OrderStatus.QualityCheck);

        var sheetId = GetCurrentSheetId(dbPath, orderId);
        using (var context = CreateContext(dbPath))
        {
            await new ValidateWorkshopSheetQcUseCase(new OrderRepository(context), new EfTransactionRunner(context))
                .ExecuteAsync(new ValidateWorkshopSheetQcCommand { WorkshopSheetId = sheetId, Comment = "Conforme" });
        }

        var result = await AdvanceAsync(dbPath, orderId, OrderStatus.QualityCheck, OrderStatus.Ready);

        result.OrderFound.Should().BeTrue();
        result.NewStatus.Should().Be(OrderStatus.Ready);

        using var verifyContext = CreateContext(dbPath);
        verifyContext.Orders.Single(o => o.OrderId == orderId).Status.Should().Be(OrderStatus.Ready);
    }

    [Fact]
    public async Task QualityCheck_Vers_Ready_AvecFicheValideeMaisObsolete_EstRefuse()
    {
        var dbPath = PathFor("ready-obsolete.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.ToFabricate);
        await AdvanceAsync(dbPath, orderId, OrderStatus.ToFabricate, OrderStatus.InProgress);
        await AdvanceAsync(dbPath, orderId, OrderStatus.InProgress, OrderStatus.QualityCheck);

        var sheetId = GetCurrentSheetId(dbPath, orderId);
        using (var context = CreateContext(dbPath))
        {
            await new OrderRepository(context).TryTakeWorkshopSheetQcDecisionAsync(
                sheetId, WorkshopSheetQcStatus.Passed, "Conforme", DateTime.UtcNow);
        }

        // La commande change APRÈS la validation : le contrôle portait sur d'autres données.
        ChangeOrderTechnicalData(dbPath, orderId);

        var act = async () => await AdvanceAsync(dbPath, orderId, OrderStatus.QualityCheck, OrderStatus.Ready);

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .WithMessage(WorkshopSheetPolicy.ReadyRequiresUpToDateSheetMessage);

        using var verifyContext = CreateContext(dbPath);
        verifyContext.Orders.Single(o => o.OrderId == orderId).Status.Should().Be(OrderStatus.QualityCheck);
    }

    [Fact]
    public async Task QualityCheck_Vers_Ready_SansAucuneFiche_EstAutorise_ParCompatibiliteHistorique()
    {
        // Commande déjà en contrôle qualité au moment du déploiement de P3-6B : elle n'a jamais pu obtenir de
        // fiche. La bloquer immobiliserait du travail légitime ; lui fabriquer un QC validé inventerait un
        // contrôle qui n'a pas eu lieu. On autorise, sans créer aucune fiche.
        var dbPath = PathFor("historic-qc.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.QualityCheck);

        var result = await AdvanceAsync(dbPath, orderId, OrderStatus.QualityCheck, OrderStatus.Ready);

        result.NewStatus.Should().Be(OrderStatus.Ready);

        using var context = CreateContext(dbPath);
        context.Orders.Single(o => o.OrderId == orderId).Status.Should().Be(OrderStatus.Ready);
        context.WorkshopSheets.Should().BeEmpty("aucune fiche rétroactive, aucun faux contrôle qualité");
    }

    [Fact]
    public async Task Ready_Vers_Delivered_NAjoutePasDeSecondeGardeQc()
    {
        var dbPath = PathFor("delivered.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.Ready);

        var result = await AdvanceAsync(dbPath, orderId, OrderStatus.Ready, OrderStatus.Delivered);

        result.NewStatus.Should().Be(OrderStatus.Delivered);
    }

    // -------------------------------------------------------------------------------------------------------
    // Non-régression P3-6
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TransitionIllegale_ResteRefusee_AvantToutEffet()
    {
        var dbPath = PathFor("illegal.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.ToFabricate);

        var act = async () => await AdvanceAsync(dbPath, orderId, OrderStatus.ToFabricate, OrderStatus.Ready);

        await act.Should().ThrowAsync<InvalidOrderStatusTransitionException>();

        using var context = CreateContext(dbPath);
        context.Orders.Single(o => o.OrderId == orderId).Status.Should().Be(OrderStatus.ToFabricate);
        context.WorkshopSheets.Should().BeEmpty("une transition illégale ne produit aucun effet, fiche comprise");
    }

    [Fact]
    public async Task ConflitDeStatut_ResteRefuse_EtNeCreeAucuneFiche()
    {
        var dbPath = PathFor("conflict-status.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.InProgress);

        // Le poste croit la commande en ToFabricate alors qu'elle est déjà InProgress.
        var act = async () => await AdvanceAsync(dbPath, orderId, OrderStatus.ToFabricate, OrderStatus.InProgress);

        await act.Should().ThrowAsync<OrderStatusConflictException>();

        using var context = CreateContext(dbPath);
        context.WorkshopSheets.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------------------------------------

    private static long GetCurrentSheetId(string dbPath, long orderId)
    {
        using var context = CreateContext(dbPath);
        return context.WorkshopSheets.Single(w => w.OrderId == orderId && w.IsCurrent).WorkshopSheetId;
    }

    private static int CountNotifications(string dbPath)
    {
        using var context = CreateContext(dbPath);
        return context.Notifications.Count();
    }
}
