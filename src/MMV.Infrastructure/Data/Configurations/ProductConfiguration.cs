using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMV.Domain.Entities;

namespace MMV.Infrastructure.Data.Configurations;

/// <summary>
/// Configuration EF Core pour l'entité Product (Fluent API).
/// </summary>
public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.HasKey(p => p.ProductId);

        builder.Property(p => p.Reference)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(p => p.Reference)
            .IsUnique()
            .HasDatabaseName("idx_products_reference_unique");

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(p => p.Description)
            .HasMaxLength(1000);

        builder.Property(p => p.PurchasePrice)
            .HasColumnType("REAL")
            .IsRequired();

        builder.Property(p => p.SalePrice)
            .HasColumnType("REAL")
            .IsRequired();

        builder.Property(p => p.StockQuantity)
            .HasDefaultValue(0);

        builder.Property(p => p.StockAlertThreshold)
            .HasDefaultValue(5);

        builder.Property(p => p.TechnicalSpecs)
            .HasColumnType("TEXT"); // JSON stocké en TEXT

        builder.Property(p => p.IsActive)
            .HasDefaultValue(true);

        builder.HasIndex(p => p.CategoryId)
            .HasDatabaseName("idx_products_category_id");

        // Relations
        builder.HasOne(p => p.Category)
            .WithMany(pc => pc.Products)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(p => p.Supplier)
            .WithMany(s => s.Products)
            .HasForeignKey(p => p.SupplierId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(p => p.OrderItems)
            .WithOne(oi => oi.Product)
            .HasForeignKey(oi => oi.ProductId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(p => p.SaleItems)
            .WithOne(si => si.Product)
            .HasForeignKey(si => si.ProductId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(p => p.StockMovements)
            .WithOne(sm => sm.Product)
            .HasForeignKey(sm => sm.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
