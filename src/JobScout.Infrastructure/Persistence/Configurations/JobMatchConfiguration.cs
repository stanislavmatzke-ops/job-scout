using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobScout.Infrastructure.Persistence.Configurations;

internal sealed class JobMatchConfiguration : IEntityTypeConfiguration<JobMatch>
{
    public void Configure(EntityTypeBuilder<JobMatch> b)
    {
        b.HasKey(m => m.Id);
        b.Property(m => m.Id).ValueGeneratedNever();

        b.Property(m => m.TriageReason).HasMaxLength(500);

        b.HasOne<User>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
        // A shared offer must not disappear while somebody has it in their list
        b.HasOne(m => m.JobOffer).WithMany().HasForeignKey(m => m.JobOfferId).OnDelete(DeleteBehavior.Restrict);

        // History lives in the private field _events (Events is a read-only view)
        b.HasMany(m => m.Events).WithOne().HasForeignKey(e => e.JobMatchId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(m => m.Events).HasField("_events").UsePropertyAccessMode(PropertyAccessMode.Field);

        b.Ignore(m => m.IsVisible);   // computed in C#, not a column

        // Never offer the same job to the same user twice (also after the ad is "refreshed")
        b.HasIndex(m => new { m.UserId, m.JobOfferId }).IsUnique();
        // The user's list: my evaluated jobs filtered by status
        b.HasIndex(m => new { m.UserId, m.Stage, m.Status });
    }
}

internal sealed class JobMatchEventConfiguration : IEntityTypeConfiguration<JobMatchEvent>
{
    public void Configure(EntityTypeBuilder<JobMatchEvent> b)
    {
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).ValueGeneratedNever();   // ← pitfall #1: without this, adding history fails
        b.Property(e => e.Note).HasMaxLength(2000);
        b.ToTable("JobMatchEvents");
    }
}