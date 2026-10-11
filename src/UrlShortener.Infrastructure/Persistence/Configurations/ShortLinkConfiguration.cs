using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UrlShortener.Domain.Entities;

namespace UrlShortener.Infrastructure.Persistence.Configurations;

public sealed class ShortLinkConfiguration : IEntityTypeConfiguration<ShortLink>
{
    public void Configure(EntityTypeBuilder<ShortLink> builder)
    {
        builder.ToTable("ShortLinks");

        builder.HasKey(x => x.Id);
        // The domain always assigns Ids itself (Guid.NewGuid()); never let EF expect a store-generated value.
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Code)
            .HasMaxLength(ShortLink.MaxCodeLength)
            .IsRequired();

        // Database-enforced uniqueness: the single source of truth for short-code collisions.
        builder.HasIndex(x => x.Code).IsUnique();

        builder.Property(x => x.OriginalUrl)
            .HasMaxLength(ShortLink.MaxOriginalUrlLength)
            .IsRequired();

        builder.Property(x => x.OwnerId)
            .HasMaxLength(128)
            .IsRequired();

        // Supports authorization checks and a future "list my links" query.
        builder.HasIndex(x => x.OwnerId);

        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.ClickCount).IsRequired();

        builder.HasMany(x => x.ClickEvents)
            .WithOne()
            .HasForeignKey(x => x.ShortLinkId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(ShortLink.ClickEvents))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
