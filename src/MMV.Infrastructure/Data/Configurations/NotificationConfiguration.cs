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
    }
}
