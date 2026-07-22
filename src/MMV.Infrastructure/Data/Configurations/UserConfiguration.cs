using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Policies;

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
            .HasMaxLength(UserIdentityPolicy.UsernameMaxLength);

        // P3-10 — Clé métier de connexion : forme normalisée (Trim + minuscules invariantes), calculée par
        // UserIdentityPolicy. C'est elle, et non Username, qui porte l'unicité.
        builder.Property(u => u.NormalizedUsername)
            .IsRequired()
            .HasMaxLength(UserIdentityPolicy.UsernameMaxLength);

        // L'ancien index unique sur Username (idx_users_username_unique) est SUPPRIMÉ, pas seulement doublé :
        // sous la collation BINARY de SQLite il tenait « admin » et « Admin » pour deux comptes distincts, donc
        // il n'a jamais garanti l'unicité du login au sens métier. Le conserver en unique ferait en outre échouer
        // un simple changement de casse d'un login existant. Aucune preuve n'exige de garantir l'unicité de la
        // forme affichable indépendamment de la forme normalisée : un seul index unique, sur la clé métier.
        builder.HasIndex(u => u.NormalizedUsername)
            .IsUnique()
            .HasDatabaseName("idx_users_normalized_username_unique");

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

        // CreatedAt : horodatage géré par l'application (initialiseur d'entité, futur IClock).
        // Pas de défaut SQL figé au build (R-19 / P2A-1R19).

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
