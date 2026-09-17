using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMV.Domain.Entities;
using MMV.Infrastructure.Data.Portability;
using MMV.Domain.Enums;

namespace MMV.Infrastructure.Data.Configurations;

/// <summary>
/// Configuration EF Core pour l'entité SaleItem (Fluent API).
/// </summary>
public class SaleItemConfiguration : IEntityTypeConfiguration<SaleItem>
{
    private readonly ModelPortability _portability;

    /// <param name="portability">
    /// Point de sélection unique du provider, fourni par <c>OpticDbContext.OnModelCreating</c> (P4-5C).
    /// Cette configuration ne l'interroge jamais elle-même.
    /// </param>
    public SaleItemConfiguration(ModelPortability portability)
        => _portability = portability ?? throw new ArgumentNullException(nameof(portability));

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

        // P4-5C / ADR-PROD-DB-003 : colonnes monétaires.
        builder.Property(si => si.UnitPrice)
            .HasMoneyMapping(_portability, LegacySqliteMoneyStoreType.Real)
            .IsRequired();

        builder.Property(si => si.TotalPrice)
            .HasMoneyMapping(_portability, LegacySqliteMoneyStoreType.Real)
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

