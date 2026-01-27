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

        builder.Property(o => o.OrderDate)
            .HasDefaultValue(DateTime.UtcNow);

        builder.Property(o => o.TotalAmount)
            .HasColumnType("REAL");

        builder.Property(o => o.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasDefaultValue(OrderStatus.New);

        builder.Property(o => o.Notes)
            .HasMaxLength(2000);

        builder.HasIndex(o => o.CustomerId)
            .HasDatabaseName("idx_orders_customer_id");

        builder.HasIndex(o => o.Status)
            .HasDatabaseName("idx_orders_status");

        // Relations
        builder.HasOne(o => o.Customer)
            .WithMany(c => c.Orders)
            .HasForeignKey(o => o.CustomerId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(o => o.Staff)
            .WithMany(u => u.Orders)
            .HasForeignKey(o => o.StaffId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(o => o.OrderItems)
            .WithOne(oi => oi.Order)
            .HasForeignKey(oi => oi.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
