using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UrlShortener.Domain.Entities;

namespace UrlShortener.Infrastructure.Persistence.Configurations;

public sealed class ClickEventConfiguration : IEntityTypeConfiguration<ClickEvent>
{
    public void Configure(EntityTypeBuilder<ClickEvent> builder)
    {
        builder.ToTable("ClickEvents");

        builder.HasKey(x => x.Id);
        // The domain always assigns Ids itself (Guid.NewGuid()); never let EF expect a store-generated value.
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.OccurredAtUtc).IsRequired();
        builder.Property(x => x.Referrer).HasMaxLength(512);
        builder.Property(x => x.UserAgent).HasMaxLength(512);

        builder.HasIndex(x => x.ShortLinkId);
    }
}
