using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMV.Domain.Entities;

namespace MMV.Infrastructure.Data.Configurations;

/// <summary>
/// Configuration EF Core pour l'entité Notification.
/// </summary>
public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");

        builder.HasKey(n => n.NotificationId);

        builder.Property(n => n.Type)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(n => n.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(n => n.Message)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(n => n.EntityType)
            .HasMaxLength(50);

        builder.Property(n => n.IsRead)
            .IsRequired();

        builder.Property(n => n.CreatedAt)
            .IsRequired();

        builder.HasIndex(n => n.IsRead);
        builder.HasIndex(n => n.Type);
        builder.HasIndex(n => n.CreatedAt);

        // P3-8 — Index unique FILTRÉ : au plus UNE alerte de stock bas ACTIVE par produit.
        //
        // Le filtre est volontairement étroit, et chacun de ses quatre termes est indispensable :
        //
        //  • Type = 'LowStock'      — SEUL type à condition persistante. Sans ce terme, l'index contraindrait aussi
        //                             les faits historiques : une commande traverse jusqu'à quatre transitions, donc
        //                             quatre 'OrderStatusChanged' sur le MÊME EntityId, toutes légitimes. La base
        //                             refuserait la deuxième. C'est le piège principal de cette migration.
        //  • EntityType = 'Product' — EntityId est polymorphe et sans clé étrangère : « 42 » désigne le produit 42
        //                             ou la commande 42 selon ce discriminant.
        //  • EntityId IS NOT NULL   — les notifications 'Info' n'ont pas d'entité liée ; sans ce terme, la sémantique
        //                             des NULL dans un index unique deviendrait le seul rempart.
        //  • ResolvedAt IS NULL     — ne contraint que les alertes ACTIVES. C'est ce qui autorise l'historique : une
        //                             deuxième pénurie après réapprovisionnement ouvre légitimement une nouvelle
        //                             alerte, l'ancienne restant conservée et résolue.
        //
        // L'index sert aussi la lecture des alertes actives (GetActiveLowStockEntityIdsAsync), dont le prédicat
        // reprend exactement ces colonnes : aucun second index (EntityType, EntityId) n'est ajouté, il serait
        // redondant.
        builder.HasIndex(n => new { n.Type, n.EntityType, n.EntityId })
            .IsUnique()
            .HasFilter("\"Type\" = 'LowStock' AND \"EntityType\" = 'Product' AND \"EntityId\" IS NOT NULL AND \"ResolvedAt\" IS NULL")
            .HasDatabaseName("idx_notifications_active_low_stock_unique");
    }
}
