using FluentAssertions;
using MMV.Application.UseCases.Orders.AdvanceOrderStatus;
using MMV.Application.UseCases.WorkshopSheets.ValidateWorkshopSheetQc;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Domain.Interfaces.Repositories;
using MMV.Domain.Services;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.WorkshopSheets;

/// <summary>
/// P3-6B — <b>Durcissement de concurrence</b> : preuves d'invariants multi-poste que les tests d'invariants de
/// sûreté ne pouvaient pas établir, parce qu'ils n'imposaient aucun <b>ordre</b> entre les opérations.
/// </summary>
/// <remarks>
/// <para>
/// <b>Comment l'entrelacement est rendu déterministe.</b> SQLite sérialise les écritures : on ne peut pas laisser
/// deux tâches courir et espérer observer une course précise. Ces tests forcent donc l'ordre exact, soit en
/// appelant directement la primitive du repository avec la précondition qu'un poste « avait lue », soit via un
/// décorateur de repository qui exécute l'action concurrente à un point choisi du use case. Aucun délai,
/// aucun <c>Task.Delay</c>, aucune dépendance à l'ordonnanceur.
/// </para>
/// </remarks>
public sealed class WorkshopSheetConcurrencyHardeningTests : WorkshopSheetTestBase
{
    // ===========================================================================================================
    // Outillage
    // ===========================================================================================================

    private static AdvanceOrderStatusUseCase CreateUseCase(OpticDbContext context, IOrderRepository? orderRepository = null)
        => new(
            orderRepository ?? new OrderRepository(context),
            new StockMovementRepository(context),
            new UnitOfWork(context),
            new EfTransactionRunner(context),
            new EfStockMutationService(context),
            new NotificationRepository(context));

    private static AdvanceOrderStatusCommand ReadyCommand(long orderId) => new()
    {
        OrderId = orderId,
        CurrentStatus = OrderStatus.QualityCheck,
        NextStatus = OrderStatus.Ready,
        CustomerDisplayName = "Jean Dupont",
        CurrentStatusDisplay = "Contrôle qualité",
        NextStatusDisplay = "Prête"
    };

    /// <summary>Amène une commande à <c>QualityCheck</c> avec une fiche v1 <b>validée</b> et à jour.</summary>
    private static async Task<(long OrderId, long SheetId)> SeedOrderReadyForQcAsync(string dbPath)
    {
        var orderId = SeedOrder(dbPath, OrderStatus.QualityCheck);

        long sheetId;
        using (var context = CreateContext(dbPath))
        {
            var repository = new OrderRepository(context);
            var order = await repository.GetWithItemsAsync(orderId);
            var sheet = await repository.CreateNextWorkshopSheetVersionAsync(
                order!, WorkshopSheetVersionPrecondition.None, DateTime.UtcNow);
            sheetId = sheet.WorkshopSheetId;
        }

        using (var context = CreateContext(dbPath))
        {
            await new ValidateWorkshopSheetQcUseCase(new OrderRepository(context), new EfTransactionRunner(context))
                .ExecuteAsync(new ValidateWorkshopSheetQcCommand { WorkshopSheetId = sheetId, Comment = "Conforme" });
        }

        return (orderId, sheetId);
    }

    /// <summary>Simule un <b>autre poste</b> : régénère la fiche depuis sa propre connexion, puis la libère.</summary>
    private static void RegenerateFromOtherWorkstation(string dbPath, long orderId)
    {
        using var context = CreateContext(dbPath);
        var repository = new OrderRepository(context);
        var order = repository.GetWithItemsAsync(orderId).GetAwaiter().GetResult();
        var current = repository.GetCurrentWorkshopSheetAsync(orderId).GetAwaiter().GetResult();

        repository.CreateNextWorkshopSheetVersionAsync(
                order!,
                current is null ? WorkshopSheetVersionPrecondition.None : WorkshopSheetVersionPrecondition.From(current),
                DateTime.UtcNow)
            .GetAwaiter().GetResult();
    }

    private static (long? SheetId, string? Fingerprint) ReadExpectedSheet(string dbPath, long orderId)
    {
        using var context = CreateContext(dbPath);
        var sheet = context.WorkshopSheets.FirstOrDefault(w => w.OrderId == orderId && w.IsCurrent);
        return (sheet?.WorkshopSheetId, sheet?.TechnicalFingerprint);
    }

    private static OrderStatus StatusOf(string dbPath, long orderId)
    {
        using var context = CreateContext(dbPath);
        return context.Orders.Single(o => o.OrderId == orderId).Status;
    }

    // ===========================================================================================================
    // 1. Course « QC validé » vs régénération concurrente — prise atomique du statut
    // ===========================================================================================================

    [Fact]
    public async Task PriseReady_EstRefusee_SiUneNouvelleVersionEstDevenueCouranteApresLaVerification()
    {
        // LE risque corrigé : le poste A vérifie la v1 (Passed, courante), le poste B régénère une v2 Pending,
        // puis A applique sa transition. Une prise ne portant que sur Order.Status l'aurait acceptée : la
        // commande serait passée « Prête » sur la foi d'un contrôle qualité ne portant plus sur la fiche
        // autoritaire.
        var dbPath = PathFor("race-qc-regeneration.db");
        EnsureSchema(dbPath);
        var (orderId, sheetV1) = await SeedOrderReadyForQcAsync(dbPath);

        // Le poste A a lu la v1 validée…
        var expected = ReadExpectedSheet(dbPath, orderId);
        expected.SheetId.Should().Be(sheetV1);

        // … le poste B régénère entre-temps : la v2 Pending devient la version courante.
        RegenerateFromOtherWorkstation(dbPath, orderId);

        // … puis A tente sa prise de statut, toujours fondée sur la v1.
        using var context = CreateContext(dbPath);
        var outcome = await new OrderRepository(context).TryTransitionWithWorkshopSheetAsync(
            orderId, OrderStatus.QualityCheck, OrderStatus.Ready, expected.SheetId, expected.Fingerprint);

        outcome.Should().Be(OrderReadyTransitionOutcome.WorkshopSheetRequirementNotMet);
        StatusOf(dbPath, orderId).Should().Be(OrderStatus.QualityCheck);

        using var verify = CreateContext(dbPath);
        var current = verify.WorkshopSheets.Single(w => w.OrderId == orderId && w.IsCurrent);
        current.Version.Should().Be(2, "la version régénérée reste la version courante");
        current.QcStatus.Should().Be(WorkshopSheetQcStatus.Pending);
    }

    [Fact]
    public async Task PriseReady_Reussit_SiLaFichePasseeEstEncoreLaVersionCourante()
    {
        var dbPath = PathFor("race-qc-nominal.db");
        EnsureSchema(dbPath);
        var (orderId, _) = await SeedOrderReadyForQcAsync(dbPath);

        var expected = ReadExpectedSheet(dbPath, orderId);

        using var context = CreateContext(dbPath);
        var outcome = await new OrderRepository(context).TryTransitionWithWorkshopSheetAsync(
            orderId, OrderStatus.QualityCheck, OrderStatus.Ready, expected.SheetId, expected.Fingerprint);

        outcome.Should().Be(OrderReadyTransitionOutcome.Taken);
        StatusOf(dbPath, orderId).Should().Be(OrderStatus.Ready);
    }

    [Fact]
    public async Task PriseReady_DonneUnConflitDeStatut_SiLeStatutReelAChange()
    {
        // Distinction exigée : conflit de STATUT (rafraîchir la commande) ≠ exigence de fiche non satisfaite.
        var dbPath = PathFor("race-qc-status-conflict.db");
        EnsureSchema(dbPath);
        var (orderId, _) = await SeedOrderReadyForQcAsync(dbPath);

        var expected = ReadExpectedSheet(dbPath, orderId);

        using (var other = CreateContext(dbPath))
        {
            other.Orders.Single(o => o.OrderId == orderId).Status = OrderStatus.Ready;
            other.SaveChanges();
        }

        using var context = CreateContext(dbPath);
        var outcome = await new OrderRepository(context).TryTransitionWithWorkshopSheetAsync(
            orderId, OrderStatus.QualityCheck, OrderStatus.Ready, expected.SheetId, expected.Fingerprint);

        outcome.Should().Be(OrderReadyTransitionOutcome.StatusConflict);
    }

    [Fact]
    public void PriseReady_Historique_EstRefusee_SiUneFicheApparaitAvantLEcriture()
    {
        // Compatibilité historique atomique : « aucune fiche » lu par le poste A ne doit PAS autoriser Ready si
        // une fiche Pending a été créée entre-temps. L'autorisation est reconfirmée par un NOT EXISTS réel.
        var dbPath = PathFor("race-historique-refus.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.QualityCheck);

        // Le poste A ne voit aucune fiche.
        var expected = ReadExpectedSheet(dbPath, orderId);
        expected.SheetId.Should().BeNull();

        // Le poste B crée la première fiche (Pending) avant que A n'écrive.
        RegenerateFromOtherWorkstation(dbPath, orderId);

        using var context = CreateContext(dbPath);
        var outcome = new OrderRepository(context).TryTransitionWithWorkshopSheetAsync(
            orderId, OrderStatus.QualityCheck, OrderStatus.Ready, null, null).GetAwaiter().GetResult();

        outcome.Should().Be(OrderReadyTransitionOutcome.WorkshopSheetRequirementNotMet);
        StatusOf(dbPath, orderId).Should().Be(OrderStatus.QualityCheck);
    }

    [Fact]
    public async Task PriseReady_Historique_EstAutorisee_SiAucuneFicheAuMomentAtomique()
    {
        var dbPath = PathFor("race-historique-ok.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.QualityCheck);

        using var context = CreateContext(dbPath);
        var outcome = await new OrderRepository(context).TryTransitionWithWorkshopSheetAsync(
            orderId, OrderStatus.QualityCheck, OrderStatus.Ready, null, null);

        outcome.Should().Be(OrderReadyTransitionOutcome.Taken);
        StatusOf(dbPath, orderId).Should().Be(OrderStatus.Ready);
    }

    // ===========================================================================================================
    // 2. Le use case complet, avec régénération forcée entre la garde métier et la prise de statut
    // ===========================================================================================================

    /// <remarks>
    /// <b>Pourquoi la régénération se fait ici sur le contexte du use case.</b> SQLite n'admet qu'un seul
    /// écrivain : écrire depuis une seconde connexion pendant que la transaction du use case est ouverte se
    /// solde par un verrou, pas par une course. La régénération est donc injectée dans la transaction en cours,
    /// ce qui reproduit exactement l'état que l'<c>UPDATE</c> conditionnel doit rencontrer — la fiche attendue
    /// n'est plus la version courante — sans dépendre d'un ordonnancement. La preuve qu'une régénération
    /// <i>réellement</i> concurrente survit à ce refus est apportée séparément, sur connexions distinctes, par
    /// <see cref="PriseReady_EstRefusee_SiUneNouvelleVersionEstDevenueCouranteApresLaVerification"/>.
    /// </remarks>
    [Fact]
    public async Task AvancerVersReady_AvecRegenerationEntreLaGardeEtLaPrise_EstRefuseSansNotification()
    {
        var dbPath = PathFor("race-usecase.db");
        EnsureSchema(dbPath);
        var (orderId, _) = await SeedOrderReadyForQcAsync(dbPath);

        using var context = CreateContext(dbPath);
        var repository = new OrderRepository(context);
        var decorated = new RegenerateOnCurrentSheetReadRepository(repository, () => RegenerateInPlace(repository, orderId));

        var act = async () => await CreateUseCase(context, decorated).ExecuteAsync(ReadyCommand(orderId));

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .WithMessage(WorkshopSheetPolicy.ReadyRequiresUnchangedCurrentSheetMessage);

        StatusOf(dbPath, orderId).Should().Be(OrderStatus.QualityCheck, "la commande reste en contrôle qualité");

        using var verify = CreateContext(dbPath);
        verify.Notifications.Should().BeEmpty("aucune notification de passage à Prête ne doit être émise");
    }

    [Fact]
    public async Task AvancerVersReady_RefuseConcurrent_NExposeAucunDetailTechnique()
    {
        var dbPath = PathFor("race-usecase-message.db");
        EnsureSchema(dbPath);
        var (orderId, _) = await SeedOrderReadyForQcAsync(dbPath);

        using var context = CreateContext(dbPath);
        var repository = new OrderRepository(context);
        var decorated = new RegenerateOnCurrentSheetReadRepository(repository, () => RegenerateInPlace(repository, orderId));

        var act = async () => await CreateUseCase(context, decorated).ExecuteAsync(ReadyCommand(orderId));

        var thrown = (await act.Should().ThrowAsync<BusinessRuleException>()).Which;
        thrown.Message.Should().NotContainAny("SQLite", "SQL", "EF", "UPDATE", "constraint", "Exception");
    }

    /// <summary>Régénère la fiche via le repository fourni, donc dans la transaction courante.</summary>
    private static void RegenerateInPlace(IOrderRepository repository, long orderId)
    {
        var order = repository.GetWithItemsAsync(orderId).GetAwaiter().GetResult();
        var current = repository.GetCurrentWorkshopSheetAsync(orderId).GetAwaiter().GetResult();

        repository.CreateNextWorkshopSheetVersionAsync(
                order!,
                current is null ? WorkshopSheetVersionPrecondition.None : WorkshopSheetVersionPrecondition.From(current),
                DateTime.UtcNow)
            .GetAwaiter().GetResult();
    }

    // ===========================================================================================================
    // 3. Génération concurrente de version — compare-and-swap sur la version attendue
    // ===========================================================================================================

    [Fact]
    public async Task DeuxGenerationsPartantDeLaV1_ProduisentUneSeuleV2_EtUnConflit()
    {
        // Sans précondition de version, la seconde demande aurait relu MAX(Version) = 2 et créé une v3 :
        // elle se serait appuyée sur une régénération qu'elle n'a jamais vue, sans que personne ne le sache.
        var dbPath = PathFor("gen-cas.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.InProgress);

        using (var seed = CreateContext(dbPath))
        {
            var repository = new OrderRepository(seed);
            var order = await repository.GetWithItemsAsync(orderId);
            await repository.CreateNextWorkshopSheetVersionAsync(
                order!, WorkshopSheetVersionPrecondition.None, DateTime.UtcNow);
        }

        // Les DEUX postes ont chargé la v1 comme version courante.
        WorkshopSheetVersionPrecondition preconditionA, preconditionB;
        using (var read = CreateContext(dbPath))
        {
            var v1 = read.WorkshopSheets.Single(w => w.OrderId == orderId && w.IsCurrent);
            preconditionA = WorkshopSheetVersionPrecondition.From(v1);
            preconditionB = WorkshopSheetVersionPrecondition.From(v1);
        }

        using (var contextA = CreateContext(dbPath))
        {
            var repository = new OrderRepository(contextA);
            var order = await repository.GetWithItemsAsync(orderId);
            var created = await repository.CreateNextWorkshopSheetVersionAsync(order!, preconditionA, DateTime.UtcNow);
            created.Version.Should().Be(2);
        }

        using (var contextB = CreateContext(dbPath))
        {
            var repository = new OrderRepository(contextB);
            var order = await repository.GetWithItemsAsync(orderId);

            var act = async () => await repository.CreateNextWorkshopSheetVersionAsync(
                order!, preconditionB, DateTime.UtcNow);

            var thrown = (await act.Should().ThrowAsync<WorkshopSheetVersionConflictException>()).Which;
            thrown.Message.Should().NotContainAny("SQLite", "SQL", "EF", "UPDATE", "constraint");
        }

        using var verify = CreateContext(dbPath);
        var versions = verify.WorkshopSheets.Where(w => w.OrderId == orderId).OrderBy(w => w.Version).ToList();
        versions.Select(v => v.Version).Should()
            .Equal(new[] { 1, 2 }, "aucune v3 fantôme n'est créée par la demande perdante");
        versions.Single(v => v.Version == 1).IsCurrent.Should().BeFalse();
        versions.Single(v => v.Version == 2).IsCurrent.Should().BeTrue();
        versions.Count(v => v.IsCurrent).Should().Be(1);
    }

    [Fact]
    public async Task DeuxCreationsConcurrentesDeLaV1_NeProduisentQuUneSeuleVersion()
    {
        // Première version : aucune version antérieure à basculer ⇒ c'est l'unicité (OrderId, Version) qui
        // arbitre. Le perdant reçoit un conflit contrôlé, jamais un doublon.
        var dbPath = PathFor("gen-v1-concurrente.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.InProgress);

        using (var contextA = CreateContext(dbPath))
        {
            var repository = new OrderRepository(contextA);
            var order = await repository.GetWithItemsAsync(orderId);
            await repository.CreateNextWorkshopSheetVersionAsync(
                order!, WorkshopSheetVersionPrecondition.None, DateTime.UtcNow);
        }

        using (var contextB = CreateContext(dbPath))
        {
            var repository = new OrderRepository(contextB);
            var order = await repository.GetWithItemsAsync(orderId);

            // Le poste B avait lui aussi constaté « aucune fiche » : sa création vise également la v1.
            var act = async () => await repository.CreateNextWorkshopSheetVersionAsync(
                order!, WorkshopSheetVersionPrecondition.None, DateTime.UtcNow);

            await act.Should().ThrowAsync<WorkshopSheetVersionConflictException>();
        }

        using var verify = CreateContext(dbPath);
        verify.WorkshopSheets.Count(w => w.OrderId == orderId).Should().Be(1);
        verify.WorkshopSheets.Count(w => w.OrderId == orderId && w.IsCurrent).Should().Be(1);
    }

    [Fact]
    public async Task GenerationAutomatique_ResteIdempotente_QuandUneFicheExisteDeja()
    {
        // L'opération automatique (EnsureCurrentSheet) ne doit jamais créer une seconde version : elle constate
        // la fiche existante et n'écrit rien. La prise atomique du statut empêche par ailleurs deux générations
        // automatiques sur la même transition.
        var dbPath = PathFor("gen-auto-idempotent.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.ToFabricate);

        using (var context = CreateContext(dbPath))
        {
            await CreateUseCase(context).ExecuteAsync(new AdvanceOrderStatusCommand
            {
                OrderId = orderId,
                CurrentStatus = OrderStatus.ToFabricate,
                NextStatus = OrderStatus.InProgress,
                CustomerDisplayName = "Jean Dupont",
                CurrentStatusDisplay = "À fabriquer",
                NextStatusDisplay = "En fabrication"
            });
        }

        using (var context = CreateContext(dbPath))
        {
            var result = await CreateUseCase(context).ExecuteAsync(new AdvanceOrderStatusCommand
            {
                OrderId = orderId,
                CurrentStatus = OrderStatus.InProgress,
                NextStatus = OrderStatus.QualityCheck,
                CustomerDisplayName = "Jean Dupont",
                CurrentStatusDisplay = "En fabrication",
                NextStatusDisplay = "Contrôle qualité"
            });

            result.HasCreatedWorkshopSheet.Should().BeFalse("la fiche existe déjà");
        }

        using var verify = CreateContext(dbPath);
        verify.WorkshopSheets.Count(w => w.OrderId == orderId).Should().Be(1);
    }

    // ===========================================================================================================
    // Décorateur de test : hook déterministe sur la lecture de la version courante
    // ===========================================================================================================

    /// <summary>
    /// Repository de test qui exécute <c>onFirstRead</c> juste <b>après</b> la première lecture de la version
    /// courante — c'est-à-dire exactement dans la fenêtre entre la garde métier et la prise de statut.
    /// </summary>
    private sealed class RegenerateOnCurrentSheetReadRepository : IOrderRepository
    {
        private readonly IOrderRepository _inner;
        private readonly Action _onFirstRead;
        private bool _fired;

        public RegenerateOnCurrentSheetReadRepository(IOrderRepository inner, Action onFirstRead)
        {
            _inner = inner;
            _onFirstRead = onFirstRead;
        }

        public async Task<WorkshopSheet?> GetCurrentWorkshopSheetAsync(long orderId, bool includeItems = false, CancellationToken cancellationToken = default)
        {
            var sheet = await _inner.GetCurrentWorkshopSheetAsync(orderId, includeItems, cancellationToken);

            if (!_fired)
            {
                _fired = true;
                _onFirstRead();
            }

            return sheet;
        }

        // ---- Délégation pure ----------------------------------------------------------------------------------
        public Task<IList<Order>> GetAllWithItemsAsync(CancellationToken cancellationToken = default)
            => _inner.GetAllWithItemsAsync(cancellationToken);

        public Task<Order?> GetWithItemsAsync(long orderId, CancellationToken cancellationToken = default)
            => _inner.GetWithItemsAsync(orderId, cancellationToken);

        public Task<Order?> GetByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default)
            => _inner.GetByOrderNumberAsync(orderNumber, cancellationToken);

        public Task<IList<Order>> GetBySaleIdAsync(long saleId, CancellationToken cancellationToken = default)
            => _inner.GetBySaleIdAsync(saleId, cancellationToken);

        public Task<bool> TryTransitionStatusAsync(long orderId, OrderStatus expectedStatus, OrderStatus nextStatus, CancellationToken cancellationToken = default)
            => _inner.TryTransitionStatusAsync(orderId, expectedStatus, nextStatus, cancellationToken);

        public Task<OrderReadyTransitionOutcome> TryTransitionWithWorkshopSheetAsync(long orderId, OrderStatus expectedStatus, OrderStatus nextStatus, long? expectedWorkshopSheetId, string? expectedFingerprint, CancellationToken cancellationToken = default)
            => _inner.TryTransitionWithWorkshopSheetAsync(orderId, expectedStatus, nextStatus, expectedWorkshopSheetId, expectedFingerprint, cancellationToken);

        public Task<IList<Order>> GetByStatusAsync(OrderStatus status, CancellationToken cancellationToken = default)
            => _inner.GetByStatusAsync(status, cancellationToken);

        public Task<IList<Order>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
            => _inner.GetByDateRangeAsync(startDate, endDate, cancellationToken);

        public Task<IList<Order>> GetOverdueOrdersAsync(CancellationToken cancellationToken = default)
            => _inner.GetOverdueOrdersAsync(cancellationToken);

        public Task<WorkshopSheet?> GetWorkshopSheetAsync(long workshopSheetId, CancellationToken cancellationToken = default)
            => _inner.GetWorkshopSheetAsync(workshopSheetId, cancellationToken);

        public Task<IList<WorkshopSheet>> GetWorkshopSheetVersionsAsync(long orderId, CancellationToken cancellationToken = default)
            => _inner.GetWorkshopSheetVersionsAsync(orderId, cancellationToken);

        public Task<WorkshopSheet> CreateNextWorkshopSheetVersionAsync(Order order, WorkshopSheetVersionPrecondition precondition, DateTime createdAt, CancellationToken cancellationToken = default)
            => _inner.CreateNextWorkshopSheetVersionAsync(order, precondition, createdAt, cancellationToken);

        public Task<bool> TryTakeWorkshopSheetQcDecisionAsync(long workshopSheetId, WorkshopSheetQcStatus decision, string? comment, DateTime completedAt, CancellationToken cancellationToken = default)
            => _inner.TryTakeWorkshopSheetQcDecisionAsync(workshopSheetId, decision, comment, completedAt, cancellationToken);

        public Task<Order?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
            => _inner.GetByIdAsync(id, cancellationToken);

        public Task<IList<Order>> GetAllAsync(CancellationToken cancellationToken = default)
            => _inner.GetAllAsync(cancellationToken);

        public Task<Order> CreateAsync(Order entity, CancellationToken cancellationToken = default)
            => _inner.CreateAsync(entity, cancellationToken);

        public Task<Order> UpdateAsync(Order entity, CancellationToken cancellationToken = default)
            => _inner.UpdateAsync(entity, cancellationToken);

        public Task DeleteAsync(long id, CancellationToken cancellationToken = default)
            => _inner.DeleteAsync(id, cancellationToken);

        public Task DeleteAsync(Order entity, CancellationToken cancellationToken = default)
            => _inner.DeleteAsync(entity, cancellationToken);

        public Task<bool> ExistsAsync(long id, CancellationToken cancellationToken = default)
            => _inner.ExistsAsync(id, cancellationToken);
    }
}
