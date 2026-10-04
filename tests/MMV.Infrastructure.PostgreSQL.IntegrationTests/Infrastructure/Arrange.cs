using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Infrastructure.Data;

namespace MMV.Infrastructure.PostgreSQL.IntegrationTests.Infrastructure;

/// <summary>
/// Arrangement des données de test, sur le modèle de <c>WorkshopSheetTestBase.SeedOrder</c> de la suite SQLite.
/// Il ne fait que PRÉPARER l'état : ce qui est prouvé passe toujours par les implémentations de production.
/// </summary>
public static class Arrange
{
    public static async Task<long> SupplierAsync(OpticDbContext context, string name = "Fournisseur Test")
    {
        var supplier = new Supplier { Name = name };
        context.Suppliers.Add(supplier);
        await context.SaveChangesAsync();
        return supplier.SupplierId;
    }

    public static async Task<long> ProductAsync(
        OpticDbContext context, long supplierId, string reference = "MON-001", int stock = 10, string name = "Monture Alpha")
    {
        var product = new Product
        {
            Reference = reference,
            Name = name,
            Category = ProductCategoryEnum.MONTURE,
            SupplierId = supplierId,
            PurchasePrice = 20m,
            SalePrice = 60m,
            StockQuantity = stock
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();
        return product.ProductId;
    }

    /// <summary>Commande complète : fournisseur, produit, client, vente parente et une ligne.</summary>
    public static async Task<long> OrderAsync(OpticDbContext context, OrderStatus status)
    {
        var productId = await ProductAsync(context, await SupplierAsync(context));

        var customer = new Customer { FirstName = "Jean", LastName = "Dupont" };
        context.Customers.Add(customer);
        var sale = new Sale { SaleNumber = "VTE-000001", Customer = customer, FinalAmount = 100m };
        context.Sales.Add(sale);
        await context.SaveChangesAsync();

        var order = new Order
        {
            OrderNumber = "CMD-000100",
            SaleId = sale.SaleId,
            OrderDate = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc),
            Status = status,
            OrderItems = { new OrderItem { ProductId = productId, ItemType = OrderItemType.Frame, Quantity = 1, UnitPrice = 60m } }
        };
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order.OrderId;
    }
}
