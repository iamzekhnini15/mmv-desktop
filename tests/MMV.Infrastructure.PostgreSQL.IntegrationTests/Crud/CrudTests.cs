using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MMV.Domain.Constants;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.PostgreSQL.IntegrationTests.Infrastructure;
using MMV.Infrastructure.Repositories;
using Npgsql;

namespace MMV.Infrastructure.PostgreSQL.IntegrationTests.Crud;

/// <summary>
/// N5 — CRUD de chaque entité du modèle (critère 3). Création, relecture, modification et suppression passent
/// par le repository de PRODUCTION de l'entité ou de son agrégat ; chaque étape s'exécute dans un contexte neuf.
/// <para>
/// Les entités sans repository de production (<see cref="Supplement"/>, <see cref="GlassSupplement"/>,
/// <see cref="GlassPricingTier"/>, <see cref="DocumentSequence"/>) sont exercées par le <see cref="OpticDbContext"/>
/// de production, faute d'autre chemin : c'est la seule façon dont la production les touche aujourd'hui.
/// </para>
/// </summary>
public class CrudTests : PostgreSqlTestBase
{
    [PostgreSqlFact]
    public Task N5_User() => CrudAsync(
        c => new UserRepository(c),
        _ => Task.FromResult(new User
        {
            Username = "jdoe", NormalizedUsername = "JDOE", PasswordHash = "h", FirstName = "J", LastName = "Doe",
            Role = UserRole.Admin
        }),
        u => u.UserId, u => u.LastName = "Dupont", u => u.LastName, "Dupont");

    [PostgreSqlFact]
    public Task N5_Supplier() => CrudAsync(
        c => new SupplierRepository(c),
        _ => Task.FromResult(new Supplier { Name = "Essilor", ReferenceCode = "ESS" }),
        s => s.SupplierId, s => s.Phone = "0102030405", s => s.Phone, "0102030405");

    [PostgreSqlFact]
    public Task N5_ProductCategory() => CrudAsync(
        c => new ProductCategoryRepository(c),
        _ => Task.FromResult(new ProductCategory { Name = "Solaires" }),
        p => p.CategoryId, p => p.Description = "Été", p => p.Description, "Été");

    [PostgreSqlFact]
    public Task N5_Customer() => CrudAsync(
        c => new CustomerRepository(c),
        _ => Task.FromResult(new Customer { FirstName = "Ada", LastName = "Lovelace", BirthDate = new DateOnly(1815, 12, 10) }),
        c => c.CustomerId, c => c.City = "Londres", c => c.City, "Londres");

    [PostgreSqlFact]
    public Task N5_Prescription() => CrudAsync(
        c => new PrescriptionRepository(c),
        async c =>
        {
            var customer = new Customer { FirstName = "Ada", LastName = "Lovelace" };
            c.Customers.Add(customer);
            await c.SaveChangesAsync();
            return new Prescription { CustomerId = customer.CustomerId, IssueDate = new DateOnly(2026, 9, 1), OdSphere = -1.25, OdAxis = 90 };
        },
        p => p.PrescriptionId, p => p.OgSphere = 2.5, p => p.OgSphere, 2.5);

    [PostgreSqlFact]
    public Task N5_StockMovement() => CrudAsync(
        c => new StockMovementRepository(c),
        async c => new StockMovement
        {
            ProductId = await Arrange.ProductAsync(c, await Arrange.SupplierAsync(c)),
            MovementType = StockMovementType.In, Quantity = 5, CreatedAt = DateTime.UtcNow
        },
        m => m.MovementId, m => m.Reason = "Inventaire", m => m.Reason, "Inventaire");

    [PostgreSqlFact]
    public Task N5_Notification() => CrudAsync(
        c => new NotificationRepository(c),
        _ => Task.FromResult(new Notification
        {
            Type = NotificationTypes.LowStock, Title = "t", Message = "m", CreatedAt = DateTime.UtcNow
        }),
        n => n.NotificationId, n => n.IsRead = true, n => n.IsRead, true);

    /// <summary>Agrégat produit : le produit et ses trois détails (verre, lentille, accessoire).</summary>
    [PostgreSqlFact]
    public async Task N5_Product_with_GlassDetail_LensDetail_AccessoryDetail()
    {
        long supplierId;
        await using (var context = NewContext())
        {
            supplierId = await Arrange.SupplierAsync(context);
        }

        Product Build(string reference, ProductCategoryEnum category) => new()
        {
            Reference = reference, Name = reference, Category = category, SupplierId = supplierId,
            PurchasePrice = 10m, SalePrice = 25m
        };

        var glass = Build("VER-1", ProductCategoryEnum.VERRE);
        glass.GlassDetail = new GlassDetail { Index = 1.6m, PowerLimitMin = -6.00m, PowerLimitMax = 4.00m };
        var lens = Build("LEN-1", ProductCategoryEnum.LENTILLE);
        lens.LensDetail = new LensDetail { Brand = "Acuvue", BaseCurve = 8.6m, Diameter = 14.2m };
        var accessory = Build("ACC-1", ProductCategoryEnum.CLIPS);
        accessory.AccessoryDetail = new AccessoryDetail { Color = "Noir" };

        await using (var context = NewContext())
        await using (var unitOfWork = new UnitOfWork(context))
        {
            foreach (var product in new[] { glass, lens, accessory })
            {
                await unitOfWork.Products.CreateAsync(product);
            }

            await unitOfWork.SaveChangesAsync();
        }

        await using (var context = NewContext())
        {
            var products = new ProductRepository(context);
            var read = await products.GetByIdWithDetailsAsync(glass.ProductId);
            read!.GlassDetail!.Index.Should().Be(1.6m);
            read.GlassDetail.PowerLimitMin.Should().Be(-6.00m);
            (await products.GetByIdWithDetailsAsync(lens.ProductId))!.LensDetail!.BaseCurve.Should().Be(8.6m);
            (await products.GetByIdWithDetailsAsync(accessory.ProductId))!.AccessoryDetail!.Color.Should().Be("Noir");

            read.GlassDetail.Index = 1.74m;
            read.SalePrice = 99.99m;
            await context.SaveChangesAsync();
        }

        await using (var context = NewContext())
        {
            var products = new ProductRepository(context);
            var read = await products.GetByIdWithDetailsAsync(glass.ProductId);
            read!.GlassDetail!.Index.Should().Be(1.74m);
            read.SalePrice.Should().Be(99.99m);

            foreach (var id in new[] { glass.ProductId, lens.ProductId, accessory.ProductId })
            {
                await products.DeleteAsync(id);
            }

            await context.SaveChangesAsync();
        }

        await using var verify = NewContext();
        (await verify.Products.CountAsync()).Should().Be(0);
        (await verify.GlassDetails.CountAsync() + await verify.LensDetails.CountAsync() + await verify.AccessoryDetails.CountAsync())
            .Should().Be(0, "les détails suivent leur produit (cascade)");
    }

    /// <summary>Agrégat vente : la vente et ses lignes.</summary>
    [PostgreSqlFact]
    public async Task N5_Sale_with_SaleItems()
    {
        long saleId;
        await using (var context = NewContext())
        {
            var productId = await Arrange.ProductAsync(context, await Arrange.SupplierAsync(context));
            await using var unitOfWork = new UnitOfWork(context);
            var sale = new Sale
            {
                SaleNumber = "VTE-000001", FinalAmount = 120m, TotalAmount = 120m, PaymentMethod = PaymentMethod.Card,
                SaleItems = { new SaleItem { ProductId = productId, Quantity = 2, UnitPrice = 60m, TotalPrice = 120m, Sphere = -0.75 } }
            };
            await unitOfWork.Sales.CreateAsync(sale);
            await unitOfWork.SaveChangesAsync();
            saleId = sale.SaleId;
        }

        await using (var context = NewContext())
        {
            var sale = await new SaleRepository(context).GetWithItemsAsync(saleId);
            sale!.SaleItems.Single().Sphere.Should().Be(-0.75);
            sale.SaleItems.Single().Quantity = 3;
            sale.Notes = "modifiée";
            await context.SaveChangesAsync();
        }

        await using (var context = NewContext())
        {
            var sales = new SaleRepository(context);
            var sale = await sales.GetWithItemsAsync(saleId);
            sale!.Notes.Should().Be("modifiée");
            sale.SaleItems.Single().Quantity.Should().Be(3);
            await sales.DeleteAsync(saleId);
            await context.SaveChangesAsync();
        }

        await using var verify = NewContext();
        (await verify.Sales.CountAsync() + await verify.SaleItems.CountAsync()).Should().Be(0);
    }

    /// <summary>
    /// Agrégat commande : la commande, ses lignes, ses fiches atelier et leurs lignes. Les fiches sont créées et
    /// modifiées par les primitives de production (N6 8 et 9) ; elles interdisent la suppression de leur commande.
    /// </summary>
    [PostgreSqlFact]
    public async Task N5_Order_with_OrderItems_WorkshopSheets_and_WorkshopSheetItems()
    {
        long orderId;
        await using (var context = NewContext())
        {
            orderId = await Arrange.OrderAsync(context, OrderStatus.InProgress);
        }

        await using (var context = NewContext())
        {
            var orders = new OrderRepository(context);
            var order = await orders.GetWithItemsAsync(orderId);
            order!.OrderItems.Should().ContainSingle();
            var sheet = await orders.CreateNextWorkshopSheetVersionAsync(order, WorkshopSheetVersionPrecondition.None, DateTime.UtcNow);
            (await orders.TryTakeWorkshopSheetQcDecisionAsync(sheet.WorkshopSheetId, WorkshopSheetQcStatus.Passed, "ok", DateTime.UtcNow))
                .Should().BeTrue();
        }

        await using (var context = NewContext())
        {
            var orders = new OrderRepository(context);
            var sheet = await orders.GetCurrentWorkshopSheetAsync(orderId, includeItems: true);
            sheet!.Items.Should().ContainSingle();
            sheet.QcStatus.Should().Be(WorkshopSheetQcStatus.Passed);

            var order = await orders.GetWithItemsAsync(orderId);
            order!.Notes = "urgent";
            await context.SaveChangesAsync();
        }

        await using (var context = NewContext())
        {
            var orders = new OrderRepository(context);
            (await orders.GetWithItemsAsync(orderId))!.Notes.Should().Be("urgent");
            await orders.DeleteAsync(orderId);

            // Fiche atelier → commande : Restrict VOULU (WorkshopSheetConfiguration) — une commande qui porte une
            // fiche ne s'efface jamais par cascade.
            var act = () => context.SaveChangesAsync();
            (await act.Should().ThrowAsync<DbUpdateException>()).WithInnerException<PostgresException>()
                .Where(e => e.SqlState == PostgresErrorCodes.ForeignKeyViolation);
        }

        // Sans fiche, la commande se supprime et ses lignes la suivent (cascade racine → lignes).
        long bareOrderId;
        await using (var context = NewContext())
        {
            var sale = new Sale { SaleNumber = "VTE-000002", FinalAmount = 10m };
            context.Sales.Add(sale);
            await context.SaveChangesAsync();
            var order = new Order
            {
                OrderNumber = "CMD-000200", SaleId = sale.SaleId,
                OrderItems = { new OrderItem { ItemType = OrderItemType.Frame, Quantity = 1, UnitPrice = 10m } }
            };
            await new OrderRepository(context).CreateAsync(order);
            await context.SaveChangesAsync();
            bareOrderId = order.OrderId;
        }

        await using (var context = NewContext())
        {
            await new OrderRepository(context).DeleteAsync(bareOrderId);
            await context.SaveChangesAsync();
        }

        await using var verify = NewContext();
        (await verify.Orders.Select(o => o.OrderId).ToListAsync()).Should().Equal(orderId);
        (await verify.OrderItems.CountAsync(i => i.OrderId == bareOrderId)).Should().Be(0);
        (await verify.WorkshopSheets.CountAsync()).Should().Be(1);
    }

    /// <summary>Entités du référentiel tarifaire verre : aucun repository de production, contexte de production.</summary>
    [PostgreSqlFact]
    public async Task N5_Supplement_GlassSupplement_GlassPricingTier_through_the_production_context()
    {
        long glassId, supplementId;
        await using (var context = NewContext())
        {
            // Les tarifs et suppléments se rattachent au DÉTAIL verre (FK vers GlassDetails), pas au produit nu.
            glassId = await Arrange.ProductAsync(context, await Arrange.SupplierAsync(context), reference: "VER-1");
            context.GlassDetails.Add(new GlassDetail { ProductId = glassId, Index = 1.5m });
            var supplement = new Supplement { Name = "Antireflet", SupplementPrice = 35.50m };
            context.Supplements.Add(supplement);
            await context.SaveChangesAsync();
            supplementId = supplement.SupplementId;
            context.GlassSupplements.Add(new GlassSupplement { GlassId = glassId, SupplementId = supplementId });
            context.GlassPricingTiers.Add(new GlassPricingTier
            {
                GlassId = glassId, PowerMin = -2.00m, PowerMax = 2.00m, PurchasePriceGrid = 12.10m, SalePriceGrid = 45.90m
            });
            await context.SaveChangesAsync();
        }

        await using (var context = NewContext())
        {
            (await context.GlassSupplements.SingleAsync()).SupplementId.Should().Be(supplementId);
            var tier = await context.GlassPricingTiers.SingleAsync();
            tier.PowerMin.Should().Be(-2.00m);
            tier.SalePriceGrid = 49.90m;
            (await context.Supplements.SingleAsync()).SupplementPrice = 39.00m;
            await context.SaveChangesAsync();
        }

        await using (var context = NewContext())
        {
            (await context.GlassPricingTiers.SingleAsync()).SalePriceGrid.Should().Be(49.90m);
            (await context.Supplements.SingleAsync()).SupplementPrice.Should().Be(39.00m);
            context.GlassSupplements.RemoveRange(context.GlassSupplements);
            context.GlassPricingTiers.RemoveRange(context.GlassPricingTiers);
            context.Supplements.RemoveRange(context.Supplements);
            await context.SaveChangesAsync();
        }

        await using var verify = NewContext();
        (await verify.Supplements.CountAsync() + await verify.GlassSupplements.CountAsync() + await verify.GlassPricingTiers.CountAsync())
            .Should().Be(0);
    }

    /// <summary>
    /// <see cref="DocumentSequence"/> : créée par la migration (données de départ), lue et modifiée par
    /// <see cref="EfNumberSequenceService"/>. La production ne la crée ni ne la supprime jamais.
    /// </summary>
    [PostgreSqlFact]
    public async Task N5_DocumentSequence_seeded_by_migration_read_and_updated_by_production()
    {
        await using (var context = NewContext())
        {
            (await context.DocumentSequences.OrderBy(s => s.SequenceName).Select(s => s.SequenceName).ToListAsync())
                .Should().Equal("ORDER", "SALE");
            await new EfNumberSequenceService(context).NextNumberAsync("ORDER");
        }

        await using var verify = NewContext();
        var order = await verify.DocumentSequences.SingleAsync(s => s.SequenceName == "ORDER");
        order.CurrentValue.Should().Be(1);
        order.UpdatedAt.Kind.Should().Be(DateTimeKind.Utc);
    }

    private async Task CrudAsync<TEntity>(
        Func<OpticDbContext, BaseRepository<TEntity, long>> repository,
        Func<OpticDbContext, Task<TEntity>> build,
        Func<TEntity, long> idOf,
        Action<TEntity> update,
        Func<TEntity, object?> observe,
        object? expected)
        where TEntity : class
    {
        long id;
        await using (var context = NewContext())
        {
            var entity = await build(context);
            await repository(context).CreateAsync(entity);
            await context.SaveChangesAsync();
            id = idOf(entity);
        }

        await using (var context = NewContext())
        {
            var repo = repository(context);
            var read = await repo.GetByIdAsync(id);
            read.Should().NotBeNull();
            update(read!);
            await repo.UpdateAsync(read!);
            await context.SaveChangesAsync();
        }

        await using (var context = NewContext())
        {
            var repo = repository(context);
            observe((await repo.GetByIdAsync(id))!).Should().Be(expected);
            await repo.DeleteAsync(id);
            await context.SaveChangesAsync();
        }

        await using var verify = NewContext();
        (await repository(verify).ExistsAsync(id)).Should().BeFalse();
    }
}
