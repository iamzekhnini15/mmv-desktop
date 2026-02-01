using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMV.Domain.Entities;

namespace MMV.Infrastructure.Data.Configurations;

/// <summary>
/// Configuration EF Core pour l'entité GlassDetail (Fluent API).
/// </summary>
public class GlassDetailConfiguration : IEntityTypeConfiguration<GlassDetail>
{
    public void Configure(EntityTypeBuilder<GlassDetail> builder)
    {
        builder.HasKey(g => g.ProductId);

        builder.Property(g => g.Material)
            .HasConversion<string>();

        builder.Property(g => g.GlassType)
            .HasConversion<string>();

        builder.Property(g => g.Diameter)
            .HasMaxLength(20);

        builder.Property(g => g.Index)
            .HasPrecision(3, 2);

        builder.Property(g => g.PowerLimitMin)
            .HasPrecision(5, 2);

        builder.Property(g => g.PowerLimitMax)
            .HasPrecision(5, 2);

        // Relations
        builder.HasOne(g => g.Product)
            .WithOne(p => p.GlassDetail)
            .HasForeignKey<GlassDetail>(g => g.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
