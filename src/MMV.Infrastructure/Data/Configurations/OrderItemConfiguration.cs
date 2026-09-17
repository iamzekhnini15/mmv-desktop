using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMV.Domain.Entities;
using MMV.Infrastructure.Data.Portability;
using MMV.Domain.Enums;

namespace MMV.Infrastructure.Data.Configurations;

/// <summary>
/// Configuration EF Core pour l'entité OrderItem (Fluent API).
/// </summary>
public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    private readonly ModelPortability _portability;

    /// <param name="portability">
    /// Point de sélection unique du provider, fourni par <c>OpticDbContext.OnModelCreating</c> (P4-5C).
    /// Cette configuration ne l'interroge jamais elle-même.
    /// </param>
    public OrderItemConfiguration(ModelPortability portability)
        => _portability = portability ?? throw new ArgumentNullException(nameof(portability));

    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.HasKey(oi => oi.OrderItemId);

        builder.Property(oi => oi.OrderId)
            .IsRequired();

        builder.Property(oi => oi.ItemType)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(oi => oi.Quantity)
            .HasDefaultValue(1);

        // P4-5C / ADR-PROD-DB-003 : colonne monétaire.
        builder.Property(oi => oi.UnitPrice)
            .HasMoneyMapping(_portability, LegacySqliteMoneyStoreType.Real)
            .IsRequired();

        builder.Property(oi => oi.UsageType)
            .HasConversion<string>();

        // P4-5C / ADR-PROD-DB-006 X3 : valeurs optiques (double). Le littéral « REAL » est RETIRÉ — il
        // désigne 8 octets sur SQLite mais seulement 4 sur PostgreSQL. Le mapping par défaut d'un double
        // donne REAL sur SQLite (inchangé) et double precision sur PostgreSQL : aucune perte, aucun
        // littéral de moteur. Le type CLR reste double, adéquat pour une dioptrie (pas de 0,25).
        builder.Property(oi => oi.Sphere);

        builder.Property(oi => oi.Cylinder);

        builder.Property(oi => oi.Addition);

        builder.Property(oi => oi.PrismValue);

        builder.Property(oi => oi.PrismBase)
            .HasConversion<string>();

        builder.Property(oi => oi.VisualAcuity)
            .HasMaxLength(10);

        // Relations
        builder.HasOne(oi => oi.Order)
            .WithMany(o => o.OrderItems)
            .HasForeignKey(oi => oi.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // P3-4B : la ligne de commande n'est plus orphelinée quand son produit est supprimé — la base refuse la
        // suppression d'un produit référencé (Restrict). La FK reste nullable (données anciennes).
        builder.HasOne(oi => oi.Product)
            .WithMany(p => p.OrderItems)
            .HasForeignKey(oi => oi.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
