using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMV.Domain.Entities;

namespace MMV.Infrastructure.Data.Configurations;

/// <summary>
/// Configuration EF Core pour l'entité AccessoryDetail (Fluent API).
/// </summary>
public class AccessoryDetailConfiguration : IEntityTypeConfiguration<AccessoryDetail>
{
    public void Configure(EntityTypeBuilder<AccessoryDetail> builder)
    {
        builder.HasKey(a => a.ProductId);

        builder.Property(a => a.Color)
            .HasMaxLength(50);

        builder.Property(a => a.Size)
            .HasMaxLength(20);

        builder.Property(a => a.Material)
            .HasMaxLength(50);

        // Relations
        builder.HasOne(a => a.Product)
            .WithOne(p => p.AccessoryDetail)
            .HasForeignKey<AccessoryDetail>(a => a.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
