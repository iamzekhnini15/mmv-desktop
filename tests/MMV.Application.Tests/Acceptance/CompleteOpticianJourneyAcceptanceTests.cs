using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Orders.SettleOrderBalance;
using MMV.Application.UseCases.Sales.RegisterSale;
using MMV.Application.UseCases.WorkshopSheets.GetCurrentWorkshopSheet;
using MMV.Application.UseCases.WorkshopSheets.ValidateWorkshopSheetQc;
using MMV.Domain.Constants;
using MMV.Domain.Enums;
using Xunit;

namespace MMV.Application.Tests.Acceptance;

/// <summary>
/// Scénario S1 (P3-11) — parcours opticien complet, de la création du client au règlement du solde, et
/// vérification de santé du socle de recette.
/// </summary>
/// <remarks>
/// Ce scénario ne re-prouve aucune règle déjà couverte par les 1413 tests ciblés : il prouve uniquement ce qui
/// exige d'avoir <b>traversé</b> plusieurs use cases de plusieurs domaines — vente ⇄ commande ⇄ stock ⇄ atelier
/// ⇄ notification ⇄ paiement.
/// </remarks>
public sealed class CompleteOpticianJourneyAcceptanceTests : AcceptanceScenarioBase
{
    /// <summary>
    /// Lit un scalaire par ADO. Les requêtes sur <c>sqlite_master</c> et les <c>PRAGMA</c> ne sont pas
    /// composables par EF (<c>SqlQuery</c> lève sur une composition) : la lecture directe est le seul moyen
    /// d'observer le schéma réel.
    /// </summary>
    private static async Task<long> ScalarAsync(Microsoft.EntityFrameworkCore.DbContext context, string sql)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        return Convert.ToInt64(value);
    }

    /// <summary>
    /// Vérification de santé <b>unique</b> du socle de recette (§19 du prompt P3-11) : la base est réellement
    /// migrée, l'index filtré P3-8 existe, les clés étrangères sont actives, aucune table métier n'est polluée
    /// et aucun compte faible ne reste actif après le seed Production sûr.
    /// </summary>
    [Fact]
    public async Task SocleDeRecette_ProduitUneBaseMigreeSaineEtNonPolluee()
    {
        // --- Arrange
        var databasePath = CreateMigratedDatabase();

        // --- Act / Assert : lecture depuis un contexte neuf.
        await using var read = OpenFreshRead(databasePath);

        // Migrations réellement appliquées (et non un schéma reconstruit par EnsureCreated).
        var applied = (await read.Database.GetAppliedMigrationsAsync()).ToList();
        applied.Should().NotBeEmpty("le schéma doit provenir des migrations réelles");
        applied.Should().Contain(
            m => m.EndsWith("AddNormalizedUsernameAndSecureLocalUsers", StringComparison.Ordinal),
            "la dernière migration livrée (P3-10) doit être présente");

        // __EFMigrationsHistory présente — impossible avec EnsureCreated().
        var historyCount = await ScalarAsync(read,
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '__EFMigrationsHistory'");
        historyCount.Should().Be(1);

        // Index unique filtré P3-8 : créé par du SQL de migration, donc ABSENT d'une base EnsureCreated. Sa
        // présence est la preuve que le socle observe bien la base telle qu'elle est en production.
        var lowStockIndexCount = await ScalarAsync(read,
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'idx_notifications_active_low_stock_unique'");
        lowStockIndexCount.Should().Be(1);

        // Clés étrangères réellement actives sur la connexion.
        var foreignKeysOn = await ScalarAsync(read, "PRAGMA foreign_keys");
        foreignKeysOn.Should().Be(1, "les scénarios de recette opposent l'intégrité référentielle réelle");

        // Aucune table MÉTIER polluée par un seed de démonstration.
        (await read.Customers.CountAsync()).Should().Be(0);
        (await read.Suppliers.CountAsync()).Should().Be(0);
        (await read.Products.CountAsync()).Should().Be(0);
        (await read.Prescriptions.CountAsync()).Should().Be(0);
        (await read.Sales.CountAsync()).Should().Be(0);
        (await read.Orders.CountAsync()).Should().Be(0);
        (await read.WorkshopSheets.CountAsync()).Should().Be(0);
        (await read.StockMovements.CountAsync()).Should().Be(0);
        (await read.Notifications.CountAsync()).Should().Be(0);

        // --- Utilisateurs : état RÉEL d'une base migrée jusqu'à HEAD, vérifié migration par migration en P3-11.
        //
        // La migration InitialCreate insère bien un administrateur par défaut (`admin`, hash faible connu). Mais la
        // migration 20260212164646_AddCounterSaleFieldsToOrder ouvre son Up() par un
        // `DeleteData(table: "Users", keyColumn: "UserId", keyValue: 1L)` et ne le réinsère JAMAIS : le seul
        // `InsertData` de ce fichier appartient à son Down(). Une base migrée jusqu'à HEAD a donc une table
        // `Users` VIDE — mesuré ici : le compte passe de 1 à 0 exactement à cette migration.
        //
        // L'invariant de recette n'est donc pas « Users n'est pas vide », mais « aucun compte FAIBLE ACTIF ne
        // subsiste » — ce que le seed Production sûr garantit dans tous les cas.
        var users = await read.Users.AsNoTracking().ToListAsync();
        users.Where(u => u.IsActive).Should().BeEmpty(
            "aucun compte par défaut faible ne doit rester actif après le seed Production sans secret bootstrap");
    }

    /// <summary>
    /// S1 — le parcours métier complet, dans l'ordre réel, exclusivement par use cases publics.
    /// </summary>
    [Fact]
    public async Task ParcoursOpticien_DeLaCreationClientAuReglementDuSolde_EstCoherentDeBoutEnBout()
    {
        // --- Arrange : base migrée neuve, propre à ce test.
        var databasePath = CreateMigratedDatabase();
        var issueDate = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc);

        long supplierId, frameId, lensOdId, lensOgId, customerId, prescriptionId;
        long saleId, orderId;
        string saleNumber, orderNumber;
        long workshopSheetId;
        decimal remainingAfterSale;

        await using (var scope = OpenScope(databasePath))
        {
            // --- Acte 1 : créer le fournisseur (obligatoire pour créer un produit).
            supplierId = await AcceptanceActs.CreerFournisseurAsync(scope);

            // --- Acte 2 : créer les trois produits optiques.
            frameId = await AcceptanceActs.CreerProduitAsync(
                scope, supplierId, AcceptanceData.FrameReference, "Monture Alpha",
                ProductCategoryEnum.MONTURE, AcceptanceData.FrameInitialStock,
                AcceptanceData.FrameAlertThreshold, AcceptanceData.FramePrice);

            lensOdId = await AcceptanceActs.CreerProduitAsync(
                scope, supplierId, AcceptanceData.LensOdReference, "Verre progressif OD",
                ProductCategoryEnum.VERRE, AcceptanceData.LensInitialStock,
                AcceptanceData.LensAlertThreshold, AcceptanceData.LensPrice);

            lensOgId = await AcceptanceActs.CreerProduitAsync(
                scope, supplierId, AcceptanceData.LensOgReference, "Verre progressif OG",
                ProductCategoryEnum.VERRE, AcceptanceData.LensInitialStock,
                AcceptanceData.LensAlertThreshold, AcceptanceData.LensPrice);

            // --- Acte 3 : créer le client.
            customerId = await AcceptanceActs.CreerClientAsync(scope);

            // --- Acte 4 : créer l'ordonnance.
            prescriptionId = await AcceptanceActs.CreerOrdonnanceAsync(scope, customerId, issueDate);
        }

        await using (var scope = OpenScope(databasePath))
        {
            // --- Acte 5 : enregistrer la vente (monture + 2 verres, remise et acompte).
            var saleResult = await scope.RegisterSale.ExecuteAsync(
                AcceptanceActs.VenteLunettesCompletes(customerId, frameId, lensOdId, lensOgId));

            saleId = saleResult.SaleId;
            saleNumber = saleResult.SaleNumber;
            remainingAfterSale = saleResult.RemainingAmount;

            // La commande de fabrication naît de la présence de verres : elle doit exister.
            saleResult.OrderId.Should().NotBeNull("une vente contenant des verres crée une commande");
            orderId = saleResult.OrderId!.Value;
            orderNumber = saleResult.OrderNumber!;

            // Solde partiel attendu, lu depuis le résultat du use case (jamais recalculé par le test).
            remainingAfterSale.Should().Be(AcceptanceData.ExpectedRemaining);
        }

        // Chaque transition s'exécute dans sa PROPRE portée, comme chaque action utilisateur en production.
        // --- Acte 6 : New → ToFabricate.
        var toFabricate = await AcceptanceActs.AvancerStatutAsync(
            databasePath, orderId, OrderStatus.New, OrderStatus.ToFabricate);
        toFabricate.OrderFound.Should().BeTrue();
        toFabricate.NewStatus.Should().Be(OrderStatus.ToFabricate);

        // --- Acte 7 : ToFabricate → InProgress (crée la fiche atelier v1 et décrémente les verres).
        var inProgress = await AcceptanceActs.AvancerStatutAsync(
            databasePath, orderId, OrderStatus.ToFabricate, OrderStatus.InProgress);
        inProgress.HasCreatedWorkshopSheet.Should().BeTrue("la fiche v1 naît à l'entrée en fabrication");
        inProgress.CreatedStockMovementCount.Should().Be(2, "seuls les deux verres sont dans la commande");

        // --- Acte 8 : InProgress → QualityCheck.
        var qualityCheck = await AcceptanceActs.AvancerStatutAsync(
            databasePath, orderId, OrderStatus.InProgress, OrderStatus.QualityCheck);
        qualityCheck.NewStatus.Should().Be(OrderStatus.QualityCheck);

        await using (var scope = OpenScope(databasePath))
        {
            // --- Acte 9 : charger la fiche atelier courante.
            var current = await scope.GetCurrentWorkshopSheet.ExecuteAsync(new GetCurrentWorkshopSheetQuery { OrderId = orderId });
            current.SheetFound.Should().BeTrue();
            workshopSheetId = current.Sheet!.WorkshopSheetId;

            // --- Acte 10 : valider le contrôle qualité.
            var qc = await scope.ValidateWorkshopSheetQc.ExecuteAsync(new ValidateWorkshopSheetQcCommand
            {
                WorkshopSheetId = workshopSheetId,
                Comment = "Montage conforme"
            });
            qc.SheetFound.Should().BeTrue();
            qc.Sheet!.QcStatus.Should().Be(WorkshopSheetQcStatus.Passed);
        }

        // --- Acte 11 : QualityCheck → Ready (autorisé car le QC est validé et la fiche à jour).
        var ready = await AcceptanceActs.AvancerStatutAsync(
            databasePath, orderId, OrderStatus.QualityCheck, OrderStatus.Ready);
        ready.NewStatus.Should().Be(OrderStatus.Ready);

        // --- Acte 12 : Ready → Delivered.
        var delivered = await AcceptanceActs.AvancerStatutAsync(
            databasePath, orderId, OrderStatus.Ready, OrderStatus.Delivered);
        delivered.NewStatus.Should().Be(OrderStatus.Delivered);

        await using (var scope = OpenScope(databasePath))
        {
            // --- Acte 13 : régler le solde.
            var settle = await scope.SettleOrderBalance.ExecuteAsync(new SettleOrderBalanceCommand { OrderId = orderId });
            settle.OrderFound.Should().BeTrue();
            settle.AlreadySettled.Should().BeFalse();
            settle.AmountEncashed.Should().Be(AcceptanceData.ExpectedRemaining);
        }

        // --- Assert : état final relu depuis un contexte NEUF (les primitives atomiques ne mettent pas à jour
        // le change tracker : relire depuis une portée d'acte renverrait des valeurs périmées).
        await using var read = OpenFreshRead(databasePath);

        // --- Assert : vente.
        var sale = await read.Sales.AsNoTracking().SingleAsync(s => s.SaleId == saleId);
        sale.CustomerId.Should().Be(customerId);
        sale.TotalAmount.Should().Be(AcceptanceData.ExpectedTotal);
        sale.DiscountAmount.Should().Be(AcceptanceData.Discount);
        sale.FinalAmount.Should().Be(AcceptanceData.ExpectedFinal);
        sale.DepositAmount.Should().Be(AcceptanceData.ExpectedFinal, "le règlement porte l'acompte au montant final");
        sale.RemainingAmount.Should().Be(0m);
        sale.PaymentStatus.Should().Be(PaymentStatus.Paid);
        // Aucun auteur n'est propagé jusqu'à l'Application (P3-11 §13) : le champ reste vide plutôt que faux.
        sale.StaffId.Should().BeNull();
        // Dette P3-7 documentée, NON masquée : Sale.Status est un indicateur historique INITIAL, jamais
        // resynchronisé. Il reste donc AwaitingLenses alors même que la commande est Delivered et la vente payée.
        // Ce n'est pas une incohérence introduite ici : c'est le contrat P3-7 explicite.
        sale.Status.Should().Be(SaleStatus.AwaitingLenses);

        // --- Assert : lignes de vente (ordre explicite, jamais l'ordre naturel).
        var saleItems = await read.SaleItems.AsNoTracking()
            .Where(i => i.SaleId == saleId)
            .OrderBy(i => i.ProductId)
            .ToListAsync();
        saleItems.Should().HaveCount(3);
        saleItems.Should().OnlyContain(i => i.TotalPrice == i.Quantity * i.UnitPrice,
            "TotalPrice est CALCULÉ par SalePricingPolicy, jamais fourni par l'appelant");

        var frameLine = saleItems.Single(i => i.ProductId == frameId);
        frameLine.ItemType.Should().Be(OrderItemType.Frame);
        frameLine.UnitPrice.Should().Be(AcceptanceData.FramePrice);

        var odLine = saleItems.Single(i => i.ProductId == lensOdId);
        odLine.ItemType.Should().Be(OrderItemType.LensOd);
        odLine.Sphere.Should().Be(AcceptanceData.OdSphere);
        odLine.Cylinder.Should().Be(AcceptanceData.OdCylinder);
        odLine.Axis.Should().Be(AcceptanceData.OdAxis, "l'axe est persisté sous forme canonique");

        var ogLine = saleItems.Single(i => i.ProductId == lensOgId);
        ogLine.ItemType.Should().Be(OrderItemType.LensOg);
        ogLine.Axis.Should().Be(AcceptanceData.OgAxis);

        // --- Assert : commande de fabrication.
        var orders = await read.Orders.AsNoTracking().Where(o => o.SaleId == saleId).ToListAsync();
        orders.Should().HaveCount(1, "une seule commande naît de cette vente");
        var order = orders.Single();
        order.OrderId.Should().Be(orderId);
        order.Status.Should().Be(OrderStatus.Delivered);

        var orderItems = await read.OrderItems.AsNoTracking()
            .Where(i => i.OrderId == orderId)
            .OrderBy(i => i.ProductId)
            .ToListAsync();
        orderItems.Should().HaveCount(2, "seules les lignes verre entrent dans la commande");
        orderItems.Select(i => i.ProductId).Should().BeEquivalentTo(new long?[] { lensOdId, lensOgId });

        // --- Assert : ordonnance intacte. Aucune FK PrescriptionId ne relie la vente à l'ordonnance (§6.2 A de
        // l'audit) : la transmission est humaine. On prouve donc seulement que rien ne l'a modifiée en collatéral.
        var prescription = await read.Prescriptions.AsNoTracking().SingleAsync(p => p.PrescriptionId == prescriptionId);
        prescription.CustomerId.Should().Be(customerId);
        prescription.OdSphere.Should().Be(AcceptanceData.OdSphere);
        prescription.OdCylinder.Should().Be(AcceptanceData.OdCylinder);
        prescription.OdAxis.Should().Be(AcceptanceData.OdAxis);
        prescription.OdAddition.Should().Be(AcceptanceData.OdAddition);
        prescription.OgSphere.Should().Be(AcceptanceData.OgSphere);
        prescription.OgCylinder.Should().Be(AcceptanceData.OgCylinder);
        prescription.OgAxis.Should().Be(AcceptanceData.OgAxis);

        // --- Assert : stock — la monture est sortie à la VENTE, les verres à la FABRICATION.
        var frame = await read.Products.AsNoTracking().SingleAsync(p => p.ProductId == frameId);
        var lensOd = await read.Products.AsNoTracking().SingleAsync(p => p.ProductId == lensOdId);
        var lensOg = await read.Products.AsNoTracking().SingleAsync(p => p.ProductId == lensOgId);
        frame.StockQuantity.Should().Be(AcceptanceData.FrameInitialStock - 1);
        lensOd.StockQuantity.Should().Be(AcceptanceData.LensInitialStock - 1);
        lensOg.StockQuantity.Should().Be(AcceptanceData.LensInitialStock - 1);

        // --- Assert : mouvements de stock — exactement 3, tous des sorties négatives, aucun auteur.
        var movements = await read.StockMovements.AsNoTracking()
            .OrderBy(m => m.ProductId).ThenBy(m => m.MovementId)
            .ToListAsync();
        movements.Should().HaveCount(3);
        movements.Should().OnlyContain(m => m.MovementType == StockMovementType.Out && m.Quantity < 0);
        movements.Should().OnlyContain(m => m.PerformedByUserId == null,
            "aucun auteur n'est propagé jusqu'à l'Application (P3-11 §13)");

        var frameMovements = movements.Where(m => m.ProductId == frameId).ToList();
        frameMovements.Should().HaveCount(1);
        frameMovements.Single().Reason.Should().Contain(saleNumber, "le lien à la vente est TEXTUEL (dette D5)");

        var lensMovements = movements.Where(m => m.ProductId == lensOdId || m.ProductId == lensOgId).ToList();
        lensMovements.Should().HaveCount(2);
        lensMovements.Should().OnlyContain(m => m.Reason != null && m.Reason.Contains(orderNumber));

        // --- Assert : fiche atelier.
        var sheets = await read.WorkshopSheets.AsNoTracking()
            .Where(s => s.OrderId == orderId)
            .OrderBy(s => s.Version)
            .ToListAsync();
        sheets.Should().HaveCount(1);
        var sheet = sheets.Single();
        sheet.WorkshopSheetId.Should().Be(workshopSheetId);
        sheet.Version.Should().Be(1);
        sheet.IsCurrent.Should().BeTrue();
        sheet.QcStatus.Should().Be(WorkshopSheetQcStatus.Passed);
        sheet.QcCompletedAt.Should().NotBeNull();
        sheet.TechnicalFingerprint.Should().NotBeNullOrWhiteSpace();
        sheet.OrderNumberSnapshot.Should().Be(orderNumber, "la fiche est un snapshot PAR VALEUR");
        sheet.CustomerNameSnapshot.Should().Contain(AcceptanceData.CustomerLastName);

        var sheetItems = await read.WorkshopSheetItems.AsNoTracking()
            .Where(i => i.WorkshopSheetId == workshopSheetId)
            .ToListAsync();
        sheetItems.Should().NotBeEmpty();

        // --- Assert : notifications — 5 transitions + 1 paiement, toutes rattachées à la commande.
        var notifications = await read.Notifications.AsNoTracking()
            .OrderBy(n => n.NotificationId)
            .ToListAsync();
        notifications.Should().HaveCount(6);
        notifications.Should().OnlyContain(n => n.EntityType == NotificationEntityTypes.Order && n.EntityId == orderId);
        notifications.Count(n => n.Type == NotificationTypes.OrderStatusChanged).Should().Be(5);
        notifications.Count(n => n.Type == NotificationTypes.PaymentReceived).Should().Be(1);
        notifications.Should().NotContain(n => n.Type == NotificationTypes.LowStock,
            "le stock reste au-dessus des seuils, et la réconciliation n'est appelée par aucun use case du parcours");
    }
}
