using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobScout.Infrastructure.Persistence.Configurations;

internal sealed class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> b)
    {
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).ValueGeneratedNever();

        b.Property(p => p.CvFileName).HasMaxLength(260);
        b.Property(p => p.InputHash).HasMaxLength(64).IsRequired();
        b.Property(p => p.ProfileJson).IsRequired();
        b.Property(p => p.Model).HasMaxLength(100);

        b.HasOne<User>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);

        // One version number per user; "latest profile" = highest version
        b.HasIndex(p => new { p.UserId, p.Version }).IsUnique();
    }
}