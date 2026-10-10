using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobScout.Infrastructure.Persistence.Configurations;

internal sealed class StoredCvConfiguration : IEntityTypeConfiguration<StoredCv>
{
    public void Configure(EntityTypeBuilder<StoredCv> b)
    {
        b.ToTable("StoredCvs");
        b.HasKey(c => c.UserId);                               // one current CV per user
        b.Property(c => c.UserId).ValueGeneratedNever();
        b.Property(c => c.FileName).HasMaxLength(260).IsRequired();
        b.Property(c => c.MediaType).HasMaxLength(100).IsRequired();
        b.Property(c => c.Content).IsRequired();

        // Personal data leaves together with the user
        b.HasOne<User>().WithOne().HasForeignKey<StoredCv>(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
