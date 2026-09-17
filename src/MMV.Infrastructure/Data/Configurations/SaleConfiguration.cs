using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMV.Domain.Entities;
using MMV.Infrastructure.Data.Portability;
using MMV.Domain.Enums;

namespace MMV.Infrastructure.Data.Configurations;

/// <summary>
/// Configuration EF Core pour l'entité Sale (Fluent API).
/// </summary>
public class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    private readonly ModelPortability _portability;

    /// <param name="portability">
    /// Point de sélection unique du provider, fourni par <c>OpticDbContext.OnModelCreating</c> (P4-5C).
    /// Cette configuration ne l'interroge jamais elle-même.
    /// </param>
    public SaleConfiguration(ModelPortability portability)
        => _portability = portability ?? throw new ArgumentNullException(nameof(portability));

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

        // P4-5C / ADR-PROD-DB-003 : colonnes monétaires. RemainingAmount et FinalAmount sont les deux
        // colonnes sur lesquelles la BASE opère elle-même (CAS « RemainingAmount > 0 » du règlement de
        // solde et SUM(FinalAmount) — SaleRepository) : leur type physique porte une garantie métier, pas
        // seulement une fidélité de stockage. Le type SQLite est donc reconduit à l'identique.
        builder.Property(s => s.TotalAmount)
            .HasMoneyMapping(_portability, LegacySqliteMoneyStoreType.Real)
            .IsRequired();

        builder.Property(s => s.DiscountAmount)
            .HasMoneyMapping(_portability, LegacySqliteMoneyStoreType.Real)
            .HasDefaultValue(0);

        builder.Property(s => s.FinalAmount)
            .HasMoneyMapping(_portability, LegacySqliteMoneyStoreType.Real)
            .IsRequired();

        builder.Property(s => s.DepositAmount)
            .HasMoneyMapping(_portability, LegacySqliteMoneyStoreType.Real);

        builder.Property(s => s.RemainingAmount)
            .HasMoneyMapping(_portability, LegacySqliteMoneyStoreType.Real);

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

        // Relations — P3-2B : SetNull → Restrict. La suppression d'un client porteur de ventes est refusée par
        // la base elle-même ; la vente n'est plus anonymisée (CustomerId conservé). CustomerId reste nullable
        // (vente au comptoir sans client), seul le comportement de suppression change.
        builder.HasOne(s => s.Customer)
            .WithMany(c => c.Sales)
            .HasForeignKey(s => s.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

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
