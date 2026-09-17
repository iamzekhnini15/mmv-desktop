using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMV.Domain.Entities;
using MMV.Infrastructure.Data.Portability;

namespace MMV.Infrastructure.Data.Configurations;

/// <summary>
/// Configuration EF Core pour l'entité <see cref="WorkshopSheet"/> (Fluent API) — P3-6B.
/// </summary>
public class WorkshopSheetConfiguration : IEntityTypeConfiguration<WorkshopSheet>
{
    private readonly ModelPortability _portability;

    /// <param name="portability">
    /// Point de sélection unique du provider, fourni par <c>OpticDbContext.OnModelCreating</c> (P4-5C).
    /// Cette configuration ne l'interroge jamais elle-même.
    /// </param>
    public WorkshopSheetConfiguration(ModelPortability portability)
        => _portability = portability ?? throw new ArgumentNullException(nameof(portability));

    public void Configure(EntityTypeBuilder<WorkshopSheet> builder)
    {
        builder.HasKey(w => w.WorkshopSheetId);

        builder.Property(w => w.Version)
            .IsRequired();

        builder.Property(w => w.IsCurrent)
            .IsRequired()
            .HasDefaultValue(false);

        // TechnicalFingerprint : SHA-256 en hexadécimal minuscule ⇒ toujours 64 caractères.
        builder.Property(w => w.TechnicalFingerprint)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(w => w.OrderNumberSnapshot)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(w => w.CustomerNameSnapshot)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(w => w.InstructionsSnapshot)
            .HasMaxLength(2000);

        builder.Property(w => w.QcStatus)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(w => w.QcComment)
            .HasMaxLength(2000);

        // Pas d'index simple sur OrderId : l'index unique (OrderId, Version) ci-dessous a OrderId en colonne de
        // tête et sert donc déjà toutes les recherches par commande. En déclarer un second sur la même propriété
        // serait par ailleurs silencieusement écrasé par l'index filtré (EF considère qu'il s'agit du même index).

        // Unicité du couple (commande, version) : filet multi-poste anti-doublon. Deux postes générant
        // simultanément la même version ⇒ une seule insertion passe, l'autre est refusée par la base.
        builder.HasIndex(w => new { w.OrderId, w.Version })
            .IsUnique()
            .HasDatabaseName("idx_workshop_sheets_order_version_unique");

        // Index unique FILTRÉ : au plus UNE version courante par commande. C'est la garantie structurelle qu'il
        // n'existe jamais deux versions autoritaires simultanées — la bascule « ancienne à false / nouvelle à
        // true » ne peut donc pas produire d'ambiguïté, même en concurrence.
        // P4-5C / ADR-PROD-DB-006 X1 : le FILTRE est sélectionné par provider, en un point unique.
        // SQLite n'a pas de booléen (IsCurrent y est un INTEGER 0/1) ⇒ « "IsCurrent" = 1 » ; PostgreSQL
        // crée une colonne boolean et REFUSE la comparaison booléen ↔ entier ⇒ « "IsCurrent" ». Porter la
        // forme SQLite telle quelle y ferait échouer la création de l'index, donc du schéma entier.
        // La garantie reste celle de la BASE des deux côtés : seule son écriture change.
        builder.HasIndex(w => w.OrderId)
            .IsUnique()
            .HasFilter(_portability.CurrentWorkshopSheetIndexFilter)
            .HasDatabaseName("idx_workshop_sheets_current_unique");

        // Restrict : une fiche atelier est un document historique. La base refuse la suppression d'une commande
        // qui en porte une — jamais d'effacement silencieux par cascade (contrairement aux OrderItems).
        builder.HasOne(w => w.Order)
            .WithMany()
            .HasForeignKey(w => w.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        // Cascade autorisée UNIQUEMENT de la racine d'agrégat vers ses propres lignes.
        builder.HasMany(w => w.Items)
            .WithOne(i => i.WorkshopSheet)
            .HasForeignKey(i => i.WorkshopSheetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
