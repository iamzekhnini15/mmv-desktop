using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMV.Domain.Entities;
using MMV.Domain.Enums;

namespace MMV.Infrastructure.Data.Configurations;

/// <summary>
/// Configuration EF Core pour l'entité User (Fluent API).
/// </summary>
public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.UserId);

        builder.Property(u => u.Username)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(u => u.Username)
            .IsUnique()
            .HasDatabaseName("idx_users_username_unique");

        builder.Property(u => u.PasswordHash)
            .IsRequired();

        builder.Property(u => u.FirstName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(u => u.LastName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(u => u.Role)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(u => u.IsActive)
            .HasDefaultValue(true);

        builder.Property(u => u.CreatedAt)
            .HasDefaultValue(DateTime.UtcNow);

        // Relations
        builder.HasMany(u => u.Sales)
            .WithOne(s => s.Staff)
            .HasForeignKey(s => s.StaffId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(u => u.StockMovements)
            .WithOne(sm => sm.PerformedByUser)
            .HasForeignKey(sm => sm.PerformedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
