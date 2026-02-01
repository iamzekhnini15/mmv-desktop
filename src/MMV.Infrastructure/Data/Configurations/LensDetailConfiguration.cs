using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMV.Domain.Entities;

namespace MMV.Infrastructure.Data.Configurations;

/// <summary>
/// Configuration EF Core pour l'entité LensDetail (Fluent API).
/// </summary>
public class LensDetailConfiguration : IEntityTypeConfiguration<LensDetail>
{
    public void Configure(EntityTypeBuilder<LensDetail> builder)
    {
        builder.HasKey(l => l.ProductId);

        builder.Property(l => l.Brand)
            .HasMaxLength(50);

        builder.Property(l => l.Model)
            .HasMaxLength(50);

        builder.Property(l => l.Material)
            .HasConversion<string>();

        builder.Property(l => l.LensType)
            .HasConversion<string>();

        builder.Property(l => l.Diameter)
            .HasPrecision(4, 2);

        builder.Property(l => l.BaseCurve)
            .HasPrecision(4, 2);

        builder.Property(l => l.Duration)
            .HasConversion<string>();

        // Relations
        builder.HasOne(l => l.Product)
            .WithOne(p => p.LensDetail)
            .HasForeignKey<LensDetail>(l => l.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
