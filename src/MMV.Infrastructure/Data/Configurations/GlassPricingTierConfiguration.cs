using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMV.Domain.Entities;

namespace MMV.Infrastructure.Data.Configurations;

/// <summary>
/// Configuration EF Core pour l'entité GlassPricingTier (Fluent API).
/// Grille tarifaire des verres selon la puissance de correction.
/// </summary>
public class GlassPricingTierConfiguration : IEntityTypeConfiguration<GlassPricingTier>
{
    public void Configure(EntityTypeBuilder<GlassPricingTier> builder)
    {
        builder.HasKey(t => t.TierId);

        builder.Property(t => t.PowerMin)
            .HasPrecision(5, 2)
            .IsRequired();

        builder.Property(t => t.PowerMax)
            .HasPrecision(5, 2)
            .IsRequired();

        builder.Property(t => t.PurchasePriceGrid)
            .IsRequired();

        builder.Property(t => t.SalePriceGrid)
            .IsRequired();

        // Relations
        builder.HasOne(t => t.Glass)
            .WithMany(g => g.PricingTiers)
            .HasForeignKey(t => t.GlassId)
            .OnDelete(DeleteBehavior.Cascade);

        // Index pour optimiser les recherches par plage
        builder.HasIndex(t => new { t.GlassId, t.PowerMin, t.PowerMax })
            .HasDatabaseName("idx_glass_pricing_tier_range");
    }
}
