using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Infrastructure.PostgreSQL.IntegrationTests.Infrastructure;
using MMV.Infrastructure.Repositories;

namespace MMV.Infrastructure.PostgreSQL.IntegrationTests.Resilience;

/// <summary>
/// P4-10 (O13, critère de sortie 8) — scénarios de la roadmap §P4-10 joués par plusieurs <b>processus OS</b>
/// (<see cref="Workstations"/>), partis ensemble, contre un vrai PostgreSQL et les implémentations de production.
/// Chaque test vérifie (1) qu'un seul poste l'emporte là où la règle l'exige, (2) l'issue explicite des perdants,
/// (3) l'état final relu par une connexion indépendante.
/// </summary>
public sealed class MultiProcessTests : PostgreSqlTestBase
{
    private string Cs => Database.ConnectionString;

    private static string[] Run(params object[] args) => args.Select(a => Convert.ToString(a, System.Globalization.CultureInfo.InvariantCulture)!).ToArray();

    private async Task<long> ProductAsync(int stock, string reference = "MON-001")
    {
        await using var context = NewContext();
        return await Arrange.ProductAsync(context, await Arrange.SupplierAsync(context), reference, stock);
    }

    private async Task<T> ReadAsync<T>(Func<MMV.Infrastructure.Data.OpticDbContext, Task<T>> read)
    {
        await using var context = NewContext();
        return await read(context);
    }

    [PostgreSqlFact]
    public async Task Four_workstations_selling_the_last_product_sell_it_exactly_once()
    {
        var productId = await ProductAsync(stock: 1);

        var results = await Workstations.RaceAsync(Cs, Enumerable.Range(0, 4).Select(_ => Run("sale", productId)).ToList());

        results.Should().ContainSingle(r => r == "ok:VTE-000001").And.HaveCount(4);
        results.Count(r => r == "insufficient").Should().Be(3, string.Join(" | ", results));
        (await ReadAsync(c => c.Products.Where(p => p.ProductId == productId).Select(p => p.StockQuantity).SingleAsync())).Should().Be(0);
        (await ReadAsync(c => c.Sales.CountAsync())).Should().Be(1, "les ventes perdantes sont annulées entièrement");
        (await ReadAsync(c => c.DocumentSequences.Where(s => s.SequenceName == "SALE").Select(s => s.CurrentValue).SingleAsync()))
            .Should().Be(1, "aucun numéro consommé par une vente annulée");
    }

    [PostgreSqlFact]
    public async Task Four_workstations_advancing_the_same_order_take_the_transition_once()
    {
        long orderId;
        await using (var context = NewContext())
        {
            orderId = await Arrange.OrderAsync(context, OrderStatus.New);
        }

        var results = await Workstations.RaceAsync(Cs,
            Enumerable.Range(0, 4).Select(_ => Run("advance", orderId, OrderStatus.New, OrderStatus.ToFabricate)).ToList());

        results.Count(r => r == "taken").Should().Be(1, string.Join(" | ", results));
        results.Count(r => r == "lost").Should().Be(3);
        (await ReadAsync(c => c.Orders.Where(o => o.OrderId == orderId).Select(o => o.Status).SingleAsync())).Should().Be(OrderStatus.ToFabricate);
    }

    [PostgreSqlFact]
    public async Task Four_workstations_generating_the_current_workshop_sheet_from_the_same_view_create_one_version()
    {
        long orderId;
        await using (var context = NewContext())
        {
            orderId = await Arrange.OrderAsync(context, OrderStatus.InProgress);
            var orders = new OrderRepository(context);
            await orders.CreateNextWorkshopSheetVersionAsync((await orders.GetWithItemsAsync(orderId))!,
                WorkshopSheetVersionPrecondition.None, DateTime.UtcNow);
        }

        var results = await Workstations.RaceAsync(Cs, Enumerable.Range(0, 4).Select(_ => Run("sheet", orderId)).ToList());

        results.Count(r => r == "created:v2").Should().Be(1, string.Join(" | ", results));
        results.Count(r => r == "conflict").Should().Be(3);
        (await ReadAsync(c => c.WorkshopSheets.Where(w => w.OrderId == orderId).OrderBy(w => w.Version)
                .Select(w => new { w.Version, w.IsCurrent }).ToListAsync()))
            .Select(w => (w.Version, w.IsCurrent)).Should().Equal((1, false), (2, true));
    }

    [PostgreSqlFact]
    public async Task Two_workstations_deciding_the_qc_of_the_same_sheet_record_a_single_definitive_decision()
    {
        long sheetId;
        await using (var context = NewContext())
        {
            var orderId = await Arrange.OrderAsync(context, OrderStatus.QualityCheck);
            var orders = new OrderRepository(context);
            sheetId = (await orders.CreateNextWorkshopSheetVersionAsync((await orders.GetWithItemsAsync(orderId))!,
                WorkshopSheetVersionPrecondition.None, DateTime.UtcNow)).WorkshopSheetId;
        }

        var decisions = new[] { WorkshopSheetQcStatus.Passed, WorkshopSheetQcStatus.Failed };
        var results = await Workstations.RaceAsync(Cs, decisions.Select(d => Run("qc", sheetId, d)).ToList());

        results.Count(r => r == "taken").Should().Be(1, string.Join(" | ", results));
        var winner = decisions[results.ToList().IndexOf("taken")];
        (await ReadAsync(c => c.WorkshopSheets.Where(w => w.WorkshopSheetId == sheetId).Select(w => w.QcStatus).SingleAsync()))
            .Should().Be(winner, "la décision enregistrée est celle du poste gagnant, jamais écrasée");
    }

    [PostgreSqlFact]
    public async Task Four_workstations_settling_the_same_balance_settle_it_once()
    {
        long saleId;
        await using (var context = NewContext())
        {
            var sale = new Sale { SaleNumber = "V-1", FinalAmount = 80m, DepositAmount = 30m, RemainingAmount = 50m };
            context.Sales.Add(sale);
            await context.SaveChangesAsync();
            saleId = sale.SaleId;
        }

        var results = await Workstations.RaceAsync(Cs, Enumerable.Range(0, 4).Select(_ => Run("settle", saleId)).ToList());

        results.Count(r => r == "taken").Should().Be(1, string.Join(" | ", results));
        (await ReadAsync(c => c.Sales.Where(s => s.SaleId == saleId).Select(s => s.RemainingAmount).SingleAsync())).Should().Be(0m);
    }

    [PostgreSqlFact]
    public async Task Four_workstations_raising_the_same_low_stock_alert_create_one_active_alert()
    {
        var productId = await ProductAsync(stock: 1);

        var results = await Workstations.RaceAsync(Cs, Enumerable.Range(0, 4).Select(_ => Run("lowstock", productId)).ToList());

        results.Count(r => r == "created").Should().Be(1, string.Join(" | ", results));
        results.Count(r => r == "exists").Should().Be(3);
        (await ReadAsync(c => c.Notifications.CountAsync(n => n.EntityId == productId && n.ResolvedAt == null))).Should().Be(1);
    }

    [PostgreSqlFact]
    public async Task Four_workstations_creating_the_same_normalized_login_create_one_user()
    {
        var results = await Workstations.RaceAsync(Cs, new[] { "Marie", "MARIE", "marie", "mArIe" }.Select(u => Run("user", u)).ToList());

        results.Count(r => r == "created").Should().Be(1, string.Join(" | ", results));
        results.Count(r => r == "error:UniqueConstraint").Should().Be(3, "doublon classé, message assaini");
        (await ReadAsync(c => c.Users.CountAsync())).Should().Be(1);
    }

    [PostgreSqlFact]
    public async Task Supplier_deletion_racing_a_product_creation_never_leaves_an_orphan()
    {
        for (var round = 0; round < 6; round++)
        {
            long supplierId;
            await using (var context = NewContext())
            {
                supplierId = await Arrange.SupplierAsync(context, $"Fournisseur {round}");
            }

            var results = await Workstations.RaceAsync(Cs, [Run("delete-supplier", supplierId), Run("product", supplierId, $"REF-{round}")]);

            string.Join("/", results).Should().BeOneOf("deleted/error:ConstraintViolation", "refused/created");
            var supplierExists = await ReadAsync(c => c.Suppliers.AnyAsync(s => s.SupplierId == supplierId));
            var products = await ReadAsync(c => c.Products.CountAsync(p => p.SupplierId == supplierId));
            (supplierExists ? products == 1 : products == 0).Should().BeTrue($"manche {round} : {string.Join(" | ", results)}");
        }
    }

    [PostgreSqlFact]
    public async Task Crossed_writes_from_two_workstations_deadlock_and_the_server_aborts_exactly_one_entirely()
    {
        var a = await ProductAsync(stock: 10, "MON-A");
        var b = await ProductAsync(stock: 10, "MON-B");

        var results = await Workstations.RaceAsync(Cs, [Run("crossed-stock", a, b), Run("crossed-stock", b, a)], middleBarrier: true);

        results.Should().BeEquivalentTo(new[] { "ok", "error:Concurrency" }, "40P01 détecté par le serveur, classé Concurrency");
        (await ReadAsync(c => c.Products.Where(p => p.ProductId == a || p.ProductId == b).Select(p => p.StockQuantity).ToListAsync()))
            .Should().Equal(new[] { 9, 9 }, "seules les deux écritures du gagnant subsistent ; celles du perdant sont annulées");
    }

    [PostgreSqlFact]
    public async Task Workstation_killed_in_the_middle_of_a_sale_leaves_nothing_and_releases_its_locks()
    {
        var productId = await ProductAsync(stock: 1);
        var worker = Workstations.Start(Cs, null, Run("hold-sale", productId));
        try
        {
            (await worker.StandardOutput.ReadLineAsync()).Should().Be("HOLDING");
        }
        finally
        {
            worker.Kill(entireProcessTree: true);
            await worker.WaitForExitAsync();
        }

        // Le serveur détecte la session morte et annule sa transaction ; un autre poste vend ensuite normalement.
        var next = await Workstations.RaceAsync(Cs, [Run("sale", productId)]);

        next.Should().Equal("ok:VTE-000001");
        (await ReadAsync(c => c.Sales.CountAsync())).Should().Be(1);
        (await ReadAsync(c => c.Products.Where(p => p.ProductId == productId).Select(p => p.StockQuantity).SingleAsync())).Should().Be(0);
    }
}
