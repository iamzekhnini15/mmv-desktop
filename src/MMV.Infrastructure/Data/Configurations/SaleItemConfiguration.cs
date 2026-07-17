using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMV.Domain.Entities;
using MMV.Domain.Enums;

namespace MMV.Infrastructure.Data.Configurations;

/// <summary>
/// Configuration EF Core pour l'entité SaleItem (Fluent API).
/// </summary>
public class SaleItemConfiguration : IEntityTypeConfiguration<SaleItem>
{
    public void Configure(EntityTypeBuilder<SaleItem> builder)
    {
        builder.HasKey(si => si.SaleItemId);

        builder.Property(si => si.SaleId)
            .IsRequired();

        builder.Property(si => si.ItemType)
            .IsRequired()
            .HasConversion<string>()
            .HasDefaultValue(OrderItemType.Frame);

        builder.Property(si => si.Quantity)
            .IsRequired();

        builder.Property(si => si.UnitPrice)
            .HasColumnType("REAL")
            .IsRequired();

        builder.Property(si => si.TotalPrice)
            .HasColumnType("REAL")
            .IsRequired();

        // Prescription fields (nullable)
        builder.Property(si => si.UsageType)
            .HasConversion<string>();

        builder.Property(si => si.PrismBase)
            .HasConversion<string>();

        builder.Property(si => si.VisualAcuity)
            .HasMaxLength(20);

        // Relations
        builder.HasOne(si => si.Sale)
            .WithMany(s => s.SaleItems)
            .HasForeignKey(si => si.SaleId)
            .OnDelete(DeleteBehavior.Cascade);

        // P3-4B : la ligne de vente n'est plus orphelinée quand son produit est supprimé — la base refuse la
        // suppression d'un produit référencé (Restrict). La FK reste nullable (données anciennes).
        builder.HasOne(si => si.Product)
            .WithMany(p => p.SaleItems)
            .HasForeignKey(si => si.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

