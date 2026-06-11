using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMV.Domain.Entities;
using MMV.Domain.Enums;

namespace MMV.Infrastructure.Data.Configurations;

/// <summary>
/// Configuration EF Core pour l'entité Sale (Fluent API).
/// </summary>
public class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> builder)
    {
        builder.HasKey(s => s.SaleId);

        builder.Property(s => s.SaleNumber)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(s => s.SaleNumber)
            .IsUnique()
            .HasDatabaseName("idx_sales_sale_number_unique");

        // SaleDate : horodatage géré par l'application (initialiseur d'entité, futur IClock).
        // Pas de défaut SQL figé au build (R-19 / P2A-1R19).

        builder.Property(s => s.TotalAmount)
            .HasColumnType("REAL")
            .IsRequired();

        builder.Property(s => s.DiscountAmount)
            .HasColumnType("REAL")
            .HasDefaultValue(0);

        builder.Property(s => s.FinalAmount)
            .HasColumnType("REAL")
            .IsRequired();

        builder.Property(s => s.DepositAmount)
            .HasColumnType("REAL");

        builder.Property(s => s.RemainingAmount)
            .HasColumnType("REAL");

        builder.Property(s => s.PaymentMethod)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(s => s.PaymentStatus)
            .HasConversion<string>()
            .HasDefaultValue(PaymentStatus.Paid);

        builder.Property(s => s.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasDefaultValue(SaleStatus.Draft);

        builder.Property(s => s.Notes)
            .HasMaxLength(2000);

        builder.HasIndex(s => s.SaleDate)
            .HasDatabaseName("idx_sales_sale_date");

        // Relations
        builder.HasOne(s => s.Customer)
            .WithMany(c => c.Sales)
            .HasForeignKey(s => s.CustomerId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(s => s.Staff)
            .WithMany(u => u.Sales)
            .HasForeignKey(s => s.StaffId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(s => s.SaleItems)
            .WithOne(si => si.Sale)
            .HasForeignKey(si => si.SaleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(s => s.Orders)
            .WithOne(o => o.Sale)
            .HasForeignKey(o => o.SaleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
