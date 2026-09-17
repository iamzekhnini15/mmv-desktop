using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMV.Domain.Entities;
using MMV.Infrastructure.Data.Portability;

namespace MMV.Infrastructure.Data.Configurations;

/// <summary>
/// Configuration EF Core pour l'entité GlassPricingTier (Fluent API).
/// Grille tarifaire des verres selon la puissance de correction.
/// </summary>
public class GlassPricingTierConfiguration : IEntityTypeConfiguration<GlassPricingTier>
{
    private readonly ModelPortability _portability;

    /// <param name="portability">
    /// Point de sélection unique du provider, fourni par <c>OpticDbContext.OnModelCreating</c> (P4-5C).
    /// Cette configuration ne l'interroge jamais elle-même.
    /// </param>
    public GlassPricingTierConfiguration(ModelPortability portability)
        => _portability = portability ?? throw new ArgumentNullException(nameof(portability));

    public void Configure(EntityTypeBuilder<GlassPricingTier> builder)
    {
        builder.HasKey(t => t.TierId);

        // PowerMin / PowerMax sont des colonnes de PUISSANCE, pas de prix : leur HasPrecision(5,2)
        // existant est conservé sans changement (ADR-PROD-DB-003 §6).
        builder.Property(t => t.PowerMin)
            .HasPrecision(5, 2)
            .IsRequired();

        builder.Property(t => t.PowerMax)
            .HasPrecision(5, 2)
            .IsRequired();

        // P4-5C / ADR-PROD-DB-003 : colonnes monétaires sans type déclaré jusqu'ici (TEXT sur SQLite),
        // défaut reconduit tel quel sur SQLite, numeric(12,2) sur PostgreSQL.
        builder.Property(t => t.PurchasePriceGrid)
            .HasMoneyMapping(_portability, LegacySqliteMoneyStoreType.ProviderDefault)
            .IsRequired();

        builder.Property(t => t.SalePriceGrid)
            .HasMoneyMapping(_portability, LegacySqliteMoneyStoreType.ProviderDefault)
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
