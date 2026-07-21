using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMV.Domain.Entities;

namespace MMV.Infrastructure.Data.Configurations;

/// <summary>
/// Configuration EF Core pour l'entité Supplier (Fluent API).
/// </summary>
public class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.HasKey(s => s.SupplierId);

        builder.Property(s => s.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(s => s.ContactEmail)
            .HasMaxLength(254);

        builder.Property(s => s.Phone)
            .HasMaxLength(20);

        builder.Property(s => s.Address)
            .HasMaxLength(500);

        builder.Property(s => s.ReferenceCode)
            .HasMaxLength(50);

        // Relations
        // P3-9 : la relation Supplier ↔ Product est configurée d'un SEUL côté, dans ProductConfiguration
        // (IsRequired + DeleteBehavior.Restrict). Un HasMany(...).OnDelete(DeleteBehavior.SetNull) vivait ici et
        // n'a JAMAIS eu d'effet : le modèle effectif — snapshot, migration ProductSchemaRefactoring et
        // PRAGMA foreign_key_list('Products') relevé sur une base réelle — porte bien RESTRICT. Il était de plus
        // inapplicable, SetNull exigeant une FK nullable alors que Product.SupplierId est un long non nullable.
        // Code trompeur retiré : il décrivait un comportement inexistant. Aucune migration, aucun changement de
        // modèle (has-pending-model-changes reste vide).
    }
}
