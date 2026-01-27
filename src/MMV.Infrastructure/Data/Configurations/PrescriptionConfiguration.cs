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

        // ŒIL DROIT (OD) - Double pour données optiques
        builder.Property(p => p.OdSphere)
            .HasColumnType("REAL");

        builder.Property(p => p.OdCylinder)
            .HasColumnType("REAL");

        builder.Property(p => p.OdAxis);

        builder.Property(p => p.OdAddition)
            .HasColumnType("REAL");

        builder.Property(p => p.OdPrismValue)
            .HasColumnType("REAL");

        builder.Property(p => p.OdPrismBase)
            .HasConversion<string>();

        builder.Property(p => p.OdVisualAcuity)
            .HasMaxLength(10);

        // ŒIL GAUCHE (OG) - Double pour données optiques
        builder.Property(p => p.OgSphere)
            .HasColumnType("REAL");

        builder.Property(p => p.OgCylinder)
            .HasColumnType("REAL");

        builder.Property(p => p.OgAxis);

        builder.Property(p => p.OgAddition)
            .HasColumnType("REAL");

        builder.Property(p => p.OgPrismValue)
            .HasColumnType("REAL");

        builder.Property(p => p.OgPrismBase)
            .HasConversion<string>();

        builder.Property(p => p.OgVisualAcuity)
            .HasMaxLength(10);

        builder.Property(p => p.Notes)
            .HasMaxLength(2000);

        builder.Property(p => p.CreatedAt)
            .HasDefaultValue(DateTime.UtcNow);

        // Relations
        builder.HasOne(p => p.Customer)
            .WithMany(c => c.Prescriptions)
            .HasForeignKey(p => p.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
