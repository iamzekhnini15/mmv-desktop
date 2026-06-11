using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMV.Domain.Entities;
using MMV.Domain.Enums;

namespace MMV.Infrastructure.Data.Configurations;

/// <summary>
/// Configuration EF Core pour l'entité StockMovement (Fluent API).
/// </summary>
public class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.HasKey(sm => sm.MovementId);

        builder.Property(sm => sm.ProductId)
            .IsRequired();

        builder.Property(sm => sm.MovementType)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(sm => sm.Quantity)
            .IsRequired();

        builder.Property(sm => sm.Reason)
            .HasMaxLength(500);

        // CreatedAt : horodatage géré par l'application (initialiseur d'entité, futur IClock).
        // Pas de défaut SQL figé au build (R-19 / P2A-1R19).

        builder.HasIndex(sm => sm.ProductId)
            .HasDatabaseName("idx_stock_movements_product_id");

        // Relations
        builder.HasOne(sm => sm.Product)
            .WithMany(p => p.StockMovements)
            .HasForeignKey(sm => sm.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(sm => sm.PerformedByUser)
            .WithMany(u => u.StockMovements)
            .HasForeignKey(sm => sm.PerformedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
