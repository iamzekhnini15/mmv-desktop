using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMV.Domain.Entities;

namespace MMV.Infrastructure.Data.Configurations;

/// <summary>
/// Configuration EF Core pour l'entité ProductCategory (Fluent API).
/// </summary>
public class ProductCategoryConfiguration : IEntityTypeConfiguration<ProductCategory>
{
    public void Configure(EntityTypeBuilder<ProductCategory> builder)
    {
        builder.HasKey(pc => pc.CategoryId);

        builder.Property(pc => pc.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.HasIndex(pc => pc.Name)
            .IsUnique()
            .HasDatabaseName("idx_product_categories_name_unique");

        builder.Property(pc => pc.Description)
            .HasMaxLength(1000);

        // Relations
        builder.HasMany(pc => pc.Products)
            .WithOne(p => p.ProductCategory)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
