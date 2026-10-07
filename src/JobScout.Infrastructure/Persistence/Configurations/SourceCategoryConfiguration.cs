using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobScout.Infrastructure.Persistence.Configurations;

internal sealed class SourceCategoryConfiguration : IEntityTypeConfiguration<SourceCategory>
{
    public void Configure(EntityTypeBuilder<SourceCategory> b)
    {
        b.HasKey(c => c.Id);
        b.Property(c => c.Id).ValueGeneratedNever();

        b.Property(c => c.Source).HasMaxLength(50).IsRequired();
        b.Property(c => c.Key).HasMaxLength(200).IsRequired();
        b.Property(c => c.Label).HasMaxLength(200).IsRequired();
        b.Property(c => c.CategoryId).HasMaxLength(200);
        b.Ignore(c => c.Exists);

        // One answer per profession per source – the learned catalogue's identity
        b.HasIndex(c => new { c.Source, c.Key }).IsUnique();
    }
}