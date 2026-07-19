using FluentAssertions;
using MMV.Application.UseCases.WorkshopSheets.GenerateWorkshopSheet;
using MMV.Application.UseCases.WorkshopSheets.RejectWorkshopSheetQc;
using MMV.Application.UseCases.WorkshopSheets.ValidateWorkshopSheetQc;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Domain.Services;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.WorkshopSheets;

/// <summary>
/// P3-6B — Contrôle qualité atelier : validation, refus, unicité de la décision, et refus des versions non
/// courantes ou obsolètes. Sur <b>vrai SQLite</b>.
/// </summary>
public sealed class WorkshopSheetQcUseCaseTests : WorkshopSheetTestBase
{
    private static async Task<long> GenerateSheetAsync(string dbPath, long orderId)
    {
        using var context = CreateContext(dbPath);
        var useCase = new GenerateWorkshopSheetUseCase(new OrderRepository(context), new EfTransactionRunner(context));
        var result = await useCase.ExecuteAsync(new GenerateWorkshopSheetCommand { OrderId = orderId });
        return result.Sheet!.WorkshopSheetId;
    }

    private static async Task<ValidateWorkshopSheetQcResult> ValidateAsync(string dbPath, long sheetId, string? comment = null)
    {
        using var context = CreateContext(dbPath);
        var useCase = new ValidateWorkshopSheetQcUseCase(new OrderRepository(context), new EfTransactionRunner(context));
        return await useCase.ExecuteAsync(new ValidateWorkshopSheetQcCommand { WorkshopSheetId = sheetId, Comment = comment });
    }

    private static async Task<RejectWorkshopSheetQcResult> RejectAsync(string dbPath, long sheetId, string? comment)
    {
        using var context = CreateContext(dbPath);
        var useCase = new RejectWorkshopSheetQcUseCase(new OrderRepository(context), new EfTransactionRunner(context));
        return await useCase.ExecuteAsync(new RejectWorkshopSheetQcCommand { WorkshopSheetId = sheetId, Comment = comment });
    }

    // -------------------------------------------------------------------------------------------------------
    // Validation
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Validate_FichePending_PasseAPassed()
    {
        var dbPath = PathFor("qc-pass.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.QualityCheck);
        var sheetId = await GenerateSheetAsync(dbPath, orderId);

        var result = await ValidateAsync(dbPath, sheetId, "Conforme");

        result.SheetFound.Should().BeTrue();
        result.Sheet!.QcStatus.Should().Be(WorkshopSheetQcStatus.Passed);
        result.Sheet.QcComment.Should().Be("Conforme");
        result.Sheet.QcCompletedAt.Should().NotBeNull();

        using var context = CreateContext(dbPath);
        var persisted = context.WorkshopSheets.Single(w => w.WorkshopSheetId == sheetId);
        persisted.QcStatus.Should().Be(WorkshopSheetQcStatus.Passed);
        persisted.QcCompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Validate_SansCommentaire_EstAcceptee()
    {
        var dbPath = PathFor("qc-pass-nocomment.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.QualityCheck);
        var sheetId = await GenerateSheetAsync(dbPath, orderId);

        var result = await ValidateAsync(dbPath, sheetId);

        result.Sheet!.QcStatus.Should().Be(WorkshopSheetQcStatus.Passed);
        result.Sheet.QcComment.Should().BeNull();
    }

    [Fact]
    public async Task Validate_FicheIntrouvable_RenvoieSheetFoundFalse()
    {
        var dbPath = PathFor("qc-notfound.db");
        EnsureSchema(dbPath);

        var result = await ValidateAsync(dbPath, sheetId: 4242);

        result.SheetFound.Should().BeFalse();
        result.Sheet.Should().BeNull();
    }

    // -------------------------------------------------------------------------------------------------------
    // Refus
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Reject_AvecCommentaire_PasseAFailed()
    {
        var dbPath = PathFor("qc-fail.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.QualityCheck);
        var sheetId = await GenerateSheetAsync(dbPath, orderId);

        var result = await RejectAsync(dbPath, sheetId, "Axe non conforme");

        result.SheetFound.Should().BeTrue();
        result.Sheet!.QcStatus.Should().Be(WorkshopSheetQcStatus.Failed);
        result.Sheet.QcComment.Should().Be("Axe non conforme");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Reject_SansCommentaire_EstRefuse_SansEcriture(string? comment)
    {
        var dbPath = PathFor($"qc-fail-nocomment-{comment?.Length ?? -1}.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.QualityCheck);
        var sheetId = await GenerateSheetAsync(dbPath, orderId);

        var act = async () => await RejectAsync(dbPath, sheetId, comment);

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .WithMessage(WorkshopSheetPolicy.QcRejectionRequiresCommentMessage);

        using var context = CreateContext(dbPath);
        context.WorkshopSheets.Single(w => w.WorkshopSheetId == sheetId)
            .QcStatus.Should().Be(WorkshopSheetQcStatus.Pending);
    }

    // -------------------------------------------------------------------------------------------------------
    // Décision unique et définitive
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Validate_DeuxFois_EstRefuseeLaSecondeFois()
    {
        var dbPath = PathFor("qc-double.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.QualityCheck);
        var sheetId = await GenerateSheetAsync(dbPath, orderId);

        await ValidateAsync(dbPath, sheetId, "Conforme");

        var act = async () => await ValidateAsync(dbPath, sheetId, "Encore conforme");

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .WithMessage(WorkshopSheetPolicy.QcAlreadyDecidedMessage);

        // Le premier commentaire n'a pas été écrasé.
        using var context = CreateContext(dbPath);
        context.WorkshopSheets.Single(w => w.WorkshopSheetId == sheetId).QcComment.Should().Be("Conforme");
    }

    [Fact]
    public async Task Reject_ApresValidation_EstRefuse()
    {
        var dbPath = PathFor("qc-fail-after-pass.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.QualityCheck);
        var sheetId = await GenerateSheetAsync(dbPath, orderId);

        await ValidateAsync(dbPath, sheetId);

        var act = async () => await RejectAsync(dbPath, sheetId, "Finalement non");

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .WithMessage(WorkshopSheetPolicy.QcAlreadyDecidedMessage);

        using var context = CreateContext(dbPath);
        context.WorkshopSheets.Single(w => w.WorkshopSheetId == sheetId)
            .QcStatus.Should().Be(WorkshopSheetQcStatus.Passed);
    }

    [Fact]
    public async Task Validate_ApresRefus_EstRefusee()
    {
        var dbPath = PathFor("qc-pass-after-fail.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.QualityCheck);
        var sheetId = await GenerateSheetAsync(dbPath, orderId);

        await RejectAsync(dbPath, sheetId, "Défaut de montage");

        var act = async () => await ValidateAsync(dbPath, sheetId);

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .WithMessage(WorkshopSheetPolicy.QcAlreadyDecidedMessage);
    }

    [Fact]
    public async Task Validate_Concurrente_UneSeuleReussite()
    {
        var dbPath = PathFor("qc-concurrent.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.QualityCheck);
        var sheetId = await GenerateSheetAsync(dbPath, orderId);

        async Task<bool> TryValidateAsync()
        {
            try
            {
                await ValidateAsync(dbPath, sheetId, "Conforme");
                return true;
            }
            catch (BusinessRuleException)
            {
                return false;
            }
        }

        var outcomes = await Task.WhenAll(TryValidateAsync(), TryValidateAsync());

        // La prise conditionnelle atomique garantit qu'une seule décision est enregistrée.
        outcomes.Count(ok => ok).Should().Be(1);

        using var context = CreateContext(dbPath);
        context.WorkshopSheets.Single(w => w.WorkshopSheetId == sheetId)
            .QcStatus.Should().Be(WorkshopSheetQcStatus.Passed);
    }

    // -------------------------------------------------------------------------------------------------------
    // Version non courante / obsolète
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Validate_VersionDevenueNonCourante_EstRefusee()
    {
        var dbPath = PathFor("qc-not-current.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.QualityCheck);
        var firstSheetId = await GenerateSheetAsync(dbPath, orderId);

        // Un autre poste régénère : la v1 n'est plus autoritaire.
        await GenerateSheetAsync(dbPath, orderId);

        var act = async () => await ValidateAsync(dbPath, firstSheetId);

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .WithMessage(WorkshopSheetPolicy.QcNotCurrentVersionMessage);

        using var context = CreateContext(dbPath);
        context.WorkshopSheets.Single(w => w.WorkshopSheetId == firstSheetId)
            .QcStatus.Should().Be(WorkshopSheetQcStatus.Pending);
    }

    [Fact]
    public async Task Validate_FicheObsolete_EstRefusee()
    {
        var dbPath = PathFor("qc-obsolete.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.QualityCheck);
        var sheetId = await GenerateSheetAsync(dbPath, orderId);

        // La commande change après la génération : la fiche ne décrit plus la fabrication réelle.
        ChangeOrderTechnicalData(dbPath, orderId);

        var act = async () => await ValidateAsync(dbPath, sheetId);

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .WithMessage(WorkshopSheetPolicy.QcObsoleteSheetMessage);

        using var context = CreateContext(dbPath);
        context.WorkshopSheets.Single(w => w.WorkshopSheetId == sheetId)
            .QcStatus.Should().Be(WorkshopSheetQcStatus.Pending);
    }

    // -------------------------------------------------------------------------------------------------------
    // Immutabilité du snapshot
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Qc_NeModifieJamaisLesDonneesSnapshot()
    {
        var dbPath = PathFor("qc-immutable.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, OrderStatus.QualityCheck);
        var sheetId = await GenerateSheetAsync(dbPath, orderId);

        string fingerprintBefore;
        string customerBefore;
        int itemCountBefore;
        using (var context = CreateContext(dbPath))
        {
            var before = context.WorkshopSheets.Single(w => w.WorkshopSheetId == sheetId);
            fingerprintBefore = before.TechnicalFingerprint;
            customerBefore = before.CustomerNameSnapshot;
            itemCountBefore = context.WorkshopSheetItems.Count(i => i.WorkshopSheetId == sheetId);
        }

        await ValidateAsync(dbPath, sheetId, "Conforme");

        using var verifyContext = CreateContext(dbPath);
        var after = verifyContext.WorkshopSheets.Single(w => w.WorkshopSheetId == sheetId);
        after.TechnicalFingerprint.Should().Be(fingerprintBefore);
        after.CustomerNameSnapshot.Should().Be(customerBefore);
        verifyContext.WorkshopSheetItems.Count(i => i.WorkshopSheetId == sheetId).Should().Be(itemCountBefore);
    }

    [Fact]
    public async Task Validate_CommandeNulle_LeveArgumentNullException()
    {
        var dbPath = PathFor("qc-nullcmd.db");
        EnsureSchema(dbPath);

        using var context = CreateContext(dbPath);
        var useCase = new ValidateWorkshopSheetQcUseCase(new OrderRepository(context), new EfTransactionRunner(context));

        var act = async () => await useCase.ExecuteAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
