using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMV.Domain.Entities;
using MMV.Domain.Enums;

namespace MMV.Infrastructure.Data.Configurations;

/// <summary>
/// Configuration EF Core pour l'entité OrderItem (Fluent API).
/// </summary>
public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.HasKey(oi => oi.OrderItemId);

        builder.Property(oi => oi.OrderId)
            .IsRequired();

        builder.Property(oi => oi.ItemType)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(oi => oi.Quantity)
            .HasDefaultValue(1);

        builder.Property(oi => oi.UnitPrice)
            .HasColumnType("REAL")
            .IsRequired();

        builder.Property(oi => oi.UsageType)
            .HasConversion<string>();

        builder.Property(oi => oi.Sphere)
            .HasColumnType("REAL");

        builder.Property(oi => oi.Cylinder)
            .HasColumnType("REAL");

        builder.Property(oi => oi.Addition)
            .HasColumnType("REAL");

        builder.Property(oi => oi.PrismValue)
            .HasColumnType("REAL");

        builder.Property(oi => oi.PrismBase)
            .HasConversion<string>();

        builder.Property(oi => oi.VisualAcuity)
            .HasMaxLength(10);

        // Relations
        builder.HasOne(oi => oi.Order)
            .WithMany(o => o.OrderItems)
            .HasForeignKey(oi => oi.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(oi => oi.Product)
            .WithMany(p => p.OrderItems)
            .HasForeignKey(oi => oi.ProductId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
