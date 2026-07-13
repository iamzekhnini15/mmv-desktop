using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMV.Domain.Entities;

namespace MMV.Infrastructure.Data.Configurations;

/// <summary>
/// Configuration EF Core pour l'entité Customer (Fluent API).
/// </summary>
public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.HasKey(c => c.CustomerId);

        builder.Property(c => c.FirstName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(c => c.LastName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(c => c.Phone)
            .HasMaxLength(20);

        builder.Property(c => c.Email)
            .HasMaxLength(254);

        builder.Property(c => c.Address)
            .HasMaxLength(500);

        builder.Property(c => c.City)
            .HasMaxLength(100);

        builder.Property(c => c.PostalCode)
            .HasMaxLength(10);

        builder.Property(c => c.SocialSecurityNumber)
            .HasMaxLength(50);

        builder.Property(c => c.InsuranceName)
            .HasMaxLength(200);

        builder.Property(c => c.Notes)
            .HasMaxLength(2000);

        // Archivage client (P3-2B) : NOT NULL, défaut false — les clients existants restent actifs.
        builder.Property(c => c.IsArchived)
            .IsRequired()
            .HasDefaultValue(false);

        // CreatedAt / UpdatedAt : horodatages gérés par l'application (initialiseur d'entité, futur IClock).
        // Pas de défaut SQL figé au build (R-19 / P2A-1R19).

        builder.HasIndex(c => c.LastName)
            .HasDatabaseName("idx_customers_lastname");

        builder.HasIndex(c => c.Phone)
            .HasDatabaseName("idx_customers_phone");

        // Relations — P3-2B : la base est le dernier rempart contre la course « check-then-delete » multi-poste.
        // Prescription : Cascade → Restrict (les ordonnances ne sont plus détruites silencieusement).
        // Sale : SetNull → Restrict (les ventes ne sont plus détachées de leur client).
        // Un client porteur d'historique ne peut donc plus être supprimé, même si le garde applicatif est
        // contourné ou perd la course : il doit être archivé (cf. SetCustomerArchivedUseCase).
        builder.HasMany(c => c.Prescriptions)
            .WithOne(p => p.Customer)
            .HasForeignKey(p => p.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.Sales)
            .WithOne(s => s.Customer)
            .HasForeignKey(s => s.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
