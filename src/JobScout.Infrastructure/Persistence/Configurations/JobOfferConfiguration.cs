using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobScout.Infrastructure.Persistence.Configurations;

internal sealed class JobOfferConfiguration : IEntityTypeConfiguration<JobOffer>
{
    public void Configure(EntityTypeBuilder<JobOffer> b)
    {
        b.HasKey(o => o.Id);
        b.Property(o => o.Id).ValueGeneratedNever();

        b.Property(o => o.Source).HasMaxLength(50).IsRequired();
        b.Property(o => o.ExternalId).HasMaxLength(100).IsRequired();
        b.Property(o => o.Url).HasMaxLength(500);
        b.Property(o => o.Title).HasMaxLength(500);
        b.Property(o => o.Company).HasMaxLength(300);
        b.Property(o => o.Location).HasMaxLength(300);
        // Tags (List<string>) → EF Core stores a list of primitives as a JSON column automatically

        // One row per ad per source – the dedup key of the whole app
        b.HasIndex(o => new { o.Source, o.ExternalId }).IsUnique();

        // Private ads added by hand (ADR-0013) – the owner's id, no navigation: only ever read through the owner's matches
        b.HasIndex(o => o.OwnerUserId);
        b.Ignore(o => o.IsPrivate);   // computed in C#, not a column
    }
}