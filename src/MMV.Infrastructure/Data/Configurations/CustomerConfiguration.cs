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

        builder.Property(c => c.CreatedAt)
            .HasDefaultValue(DateTime.UtcNow);

        builder.Property(c => c.UpdatedAt)
            .HasDefaultValue(DateTime.UtcNow);

        builder.HasIndex(c => c.LastName)
            .HasDatabaseName("idx_customers_lastname");

        builder.HasIndex(c => c.Phone)
            .HasDatabaseName("idx_customers_phone");

        // Relations
        builder.HasMany(c => c.Prescriptions)
            .WithOne(p => p.Customer)
            .HasForeignKey(p => p.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.Sales)
            .WithOne(s => s.Customer)
            .HasForeignKey(s => s.CustomerId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
