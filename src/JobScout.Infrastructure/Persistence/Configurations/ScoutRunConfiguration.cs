using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobScout.Infrastructure.Persistence.Configurations;

internal sealed class ScoutRunConfiguration : IEntityTypeConfiguration<ScoutRun>
{
    public void Configure(EntityTypeBuilder<ScoutRun> b)
    {
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).ValueGeneratedNever();
        b.Property(r => r.Error).HasMaxLength(ScoutRun.MaxErrorLength);

        b.HasOne<User>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);

        // "Last successful run of this user" – the query every run starts with
        b.HasIndex(r => new { r.UserId, r.Status, r.StartedAt });
    }
}