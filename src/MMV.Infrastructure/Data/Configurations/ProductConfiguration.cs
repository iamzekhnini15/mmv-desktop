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

        // P3-4B : l'unicité stricte porte désormais sur la représentation NORMALISÉE de la référence (Trim +
        // casse invariante), pas sur la valeur d'affichage brute. Deux références ne différant que par la casse
        // ou les espaces externes sont donc rejetées par la base (filet multi-poste), y compris entre postes.
        builder.Property(p => p.NormalizedReference)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(p => p.NormalizedReference)
            .IsUnique()
            .HasDatabaseName("idx_products_normalized_reference_unique");

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

        builder.Property(p => p.RecommendedPrice)
            .HasColumnType("REAL");

        builder.Property(p => p.Category)
            .HasConversion<string>()
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
        builder.HasOne(p => p.ProductCategory)
            .WithMany(pc => pc.Products)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(p => p.Supplier)
            .WithMany(s => s.Products)
            .HasForeignKey(p => p.SupplierId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // P3-4B : la base refuse la suppression d'un produit porteur d'historique (filet multi-poste). Les FK
        // restent nullables là où elles l'étaient (données anciennes), mais passent de SetNull/Cascade à Restrict :
        // plus d'orphelinage silencieux des lignes de vente/commande, plus d'effacement en cascade des mouvements.
        builder.HasMany(p => p.OrderItems)
            .WithOne(oi => oi.Product)
            .HasForeignKey(oi => oi.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(p => p.SaleItems)
            .WithOne(si => si.Product)
            .HasForeignKey(si => si.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(p => p.StockMovements)
            .WithOne(sm => sm.Product)
            .HasForeignKey(sm => sm.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
