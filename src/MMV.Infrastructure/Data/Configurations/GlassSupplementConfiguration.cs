using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMV.Domain.Entities;

namespace MMV.Infrastructure.Data.Configurations;

/// <summary>
/// Configuration EF Core pour l'entité GlassSupplement (Fluent API).
/// Table d'association many-to-many entre GlassDetail et Supplement.
/// </summary>
public class GlassSupplementConfiguration : IEntityTypeConfiguration<GlassSupplement>
{
    public void Configure(EntityTypeBuilder<GlassSupplement> builder)
    {
        builder.HasKey(gs => new { gs.GlassId, gs.SupplementId });

        // Relations
        builder.HasOne(gs => gs.Glass)
            .WithMany(g => g.GlassSupplements)
            .HasForeignKey(gs => gs.GlassId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(gs => gs.Supplement)
            .WithMany(s => s.GlassSupplements)
            .HasForeignKey(gs => gs.SupplementId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
