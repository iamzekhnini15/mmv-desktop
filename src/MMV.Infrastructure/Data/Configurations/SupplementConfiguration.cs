using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMV.Domain.Entities;
using MMV.Infrastructure.Data.Portability;

namespace MMV.Infrastructure.Data.Configurations;

/// <summary>
/// Configuration EF Core pour l'entité Supplement (Fluent API).
/// </summary>
public class SupplementConfiguration : IEntityTypeConfiguration<Supplement>
{
    private readonly ModelPortability _portability;

    /// <param name="portability">
    /// Point de sélection unique du provider, fourni par <c>OpticDbContext.OnModelCreating</c> (P4-5C).
    /// Cette configuration ne l'interroge jamais elle-même.
    /// </param>
    public SupplementConfiguration(ModelPortability portability)
        => _portability = portability ?? throw new ArgumentNullException(nameof(portability));

    public void Configure(EntityTypeBuilder<Supplement> builder)
    {
        builder.HasKey(s => s.SupplementId);

        builder.Property(s => s.Name)
            .IsRequired()
            .HasMaxLength(50);

        // P4-5C / ADR-PROD-DB-003 : colonne monétaire sans type déclaré jusqu'ici — mapping decimal par
        // défaut, soit TEXT sur SQLite. Ce défaut est RECONDUIT tel quel sur SQLite ; sur PostgreSQL la
        // précision seule suffit à produire numeric(12,2).
        builder.Property(s => s.SupplementPrice)
            .HasMoneyMapping(_portability, LegacySqliteMoneyStoreType.ProviderDefault)
            .IsRequired();
    }
}
