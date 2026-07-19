using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMV.Domain.Entities;

namespace MMV.Infrastructure.Data.Configurations;

/// <summary>
/// Configuration EF Core pour l'entité <see cref="WorkshopSheetItem"/> (Fluent API) — P3-6B.
/// </summary>
/// <remarks>
/// <b>Aucune clé étrangère vers <c>Product</c>.</b> Les données produit sont des <i>copies snapshot</i> : la
/// ligne survit intacte à un renommage, une désactivation ou une réorganisation du catalogue.
/// <c>SourceProductId</c> est une valeur informative, volontairement <b>non</b> contrainte.
/// </remarks>
public class WorkshopSheetItemConfiguration : IEntityTypeConfiguration<WorkshopSheetItem>
{
    public void Configure(EntityTypeBuilder<WorkshopSheetItem> builder)
    {
        builder.HasKey(i => i.WorkshopSheetItemId);

        builder.Property(i => i.Position)
            .IsRequired();

        builder.Property(i => i.ItemType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(i => i.ProductReferenceSnapshot)
            .HasMaxLength(50);

        builder.Property(i => i.ProductNameSnapshot)
            .HasMaxLength(200);

        builder.Property(i => i.ProductCategorySnapshot)
            .HasMaxLength(50);

        builder.Property(i => i.Quantity)
            .IsRequired();

        builder.Property(i => i.UsageType)
            .HasConversion<string>()
            .HasMaxLength(20);

        // Cohérent avec OrderItemConfiguration : les valeurs optiques sont stockées en REAL.
        builder.Property(i => i.SourceSphere).HasColumnType("REAL");
        builder.Property(i => i.SourceCylinder).HasColumnType("REAL");
        builder.Property(i => i.Addition).HasColumnType("REAL");
        builder.Property(i => i.PrismValue).HasColumnType("REAL");
        builder.Property(i => i.TransposedSphere).HasColumnType("REAL");
        builder.Property(i => i.TransposedCylinder).HasColumnType("REAL");

        builder.Property(i => i.PrismBase)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(i => i.VisualAcuity)
            .HasMaxLength(20);

        builder.Property(i => i.HasTransposition)
            .IsRequired()
            .HasDefaultValue(false);

        builder.HasIndex(i => i.WorkshopSheetId)
            .HasDatabaseName("idx_workshop_sheet_items_sheet_id");
    }
}
