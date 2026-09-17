using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMV.Domain.Entities;
using MMV.Domain.Enums;

namespace MMV.Infrastructure.Data.Configurations;

/// <summary>
/// Configuration EF Core pour l'entité Prescription (Fluent API).
/// </summary>
public class PrescriptionConfiguration : IEntityTypeConfiguration<Prescription>
{
    public void Configure(EntityTypeBuilder<Prescription> builder)
    {
        builder.HasKey(p => p.PrescriptionId);

        builder.Property(p => p.CustomerId)
            .IsRequired();

        builder.Property(p => p.IssueDate)
            .IsRequired();

        builder.Property(p => p.DoctorName)
            .HasMaxLength(200);

        // ŒIL DROIT (OD) — données optiques en double. P4-5C / ADR-PROD-DB-006 X3 : le littéral « REAL »
        // est RETIRÉ (8 octets sur SQLite, 4 seulement sur PostgreSQL). Le mapping par défaut d'un double
        // donne REAL sur SQLite (inchangé) et double precision sur PostgreSQL : aucune perte.
        builder.Property(p => p.OdSphere);

        builder.Property(p => p.OdCylinder);

        builder.Property(p => p.OdAxis);

        builder.Property(p => p.OdAddition);

        builder.Property(p => p.OdPrismValue);

        builder.Property(p => p.OdPrismBase)
            .HasConversion<string>();

        builder.Property(p => p.OdVisualAcuity)
            .HasMaxLength(10);

        // ŒIL GAUCHE (OG) - Double pour données optiques
        builder.Property(p => p.OgSphere);

        builder.Property(p => p.OgCylinder);

        builder.Property(p => p.OgAxis);

        builder.Property(p => p.OgAddition);

        builder.Property(p => p.OgPrismValue);

        builder.Property(p => p.OgPrismBase)
            .HasConversion<string>();

        builder.Property(p => p.OgVisualAcuity)
            .HasMaxLength(10);

        builder.Property(p => p.Notes)
            .HasMaxLength(2000);

        // CreatedAt : horodatage géré par l'application (initialiseur d'entité, futur IClock).
        // Pas de défaut SQL figé au build (R-19 / P2A-1R19).

        // Relations — P3-2B : Cascade → Restrict. La suppression d'un client porteur d'ordonnances est
        // refusée par la base elle-même ; l'ordonnance (donnée médicale) n'est plus détruite en cascade.
        builder.HasOne(p => p.Customer)
            .WithMany(c => c.Prescriptions)
            .HasForeignKey(p => p.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
