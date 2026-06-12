using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Persistence;

namespace MMV.Infrastructure.Data.Configurations;

/// <summary>
/// Configuration EF Core pour l'entité <see cref="DocumentSequence"/> (P2A-1E, R-03 / ADR-006).
///
/// <para>
/// La clé primaire est le <see cref="DocumentSequence.SequenceName"/> (nom logique). Les compteurs
/// <c>SALE</c> et <c>ORDER</c> sont <b>seedés via <c>HasData</c></b> (et non par une insertion de migration
/// manuelle) afin que les <b>deux</b> chemins de création de schéma les obtiennent :
/// <c>Database.Migrate()</c> (production / bases migrées) <b>et</b> <c>Database.EnsureCreated()</c> (tests,
/// bases historiques). La valeur de départ est <c>0</c> : le premier numéro émis est donc <c>1</c>.
/// </para>
/// </summary>
public class DocumentSequenceConfiguration : IEntityTypeConfiguration<DocumentSequence>
{
    /// <summary>
    /// Horodatage de seed <b>constant</b> (jamais <c>DateTime.Now</c>) : une valeur dynamique provoquerait
    /// une divergence perpétuelle du modèle (has-pending-model-changes) — cf. R-19. La valeur réelle est
    /// ensuite gérée par le service à chaque attribution.
    /// </summary>
    private static readonly DateTime SeedTimestamp = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public void Configure(EntityTypeBuilder<DocumentSequence> builder)
    {
        builder.ToTable("DocumentSequences");

        builder.HasKey(s => s.SequenceName);

        builder.Property(s => s.SequenceName)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(s => s.Prefix)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(s => s.CurrentValue)
            .IsRequired();

        builder.Property(s => s.UpdatedAt)
            .IsRequired();

        // Compteurs de base (P2A-1E) : aucune notion d'organisation/magasin/pays/exercice.
        builder.HasData(
            new DocumentSequence
            {
                SequenceName = DocumentSequenceNames.Sale,
                Prefix = "VTE",
                CurrentValue = 0,
                UpdatedAt = SeedTimestamp,
            },
            new DocumentSequence
            {
                SequenceName = DocumentSequenceNames.Order,
                Prefix = "CMD",
                CurrentValue = 0,
                UpdatedAt = SeedTimestamp,
            });
    }
}
