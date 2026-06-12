using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMV.Domain.Entities;
using MMV.Domain.Enums;

namespace MMV.Infrastructure.Data.Configurations;

/// <summary>
/// Configuration EF Core pour l'entité Order (Fluent API).
/// </summary>
public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.HasKey(o => o.OrderId);

        builder.Property(o => o.OrderNumber)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(o => o.OrderNumber)
            .IsUnique()
            .HasDatabaseName("idx_orders_order_number_unique");

        // OrderDate : horodatage géré par l'application (initialiseur d'entité, futur IClock).
        // Pas de défaut SQL figé au build (R-19 / P2A-1R19).

        builder.Property(o => o.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasDefaultValue(OrderStatus.New);

        builder.Property(o => o.Notes)
            .HasMaxLength(2000);

        builder.HasIndex(o => o.SaleId)
            .HasDatabaseName("idx_orders_sale_id");

        builder.HasIndex(o => o.Status)
            .HasDatabaseName("idx_orders_status");

        // Relations
        builder.HasOne(o => o.Sale)
            .WithMany(s => s.Orders)
            .HasForeignKey(o => o.SaleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(o => o.OrderItems)
            .WithOne(oi => oi.Order)
            .HasForeignKey(oi => oi.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
