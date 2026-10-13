using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobScout.Infrastructure.Persistence.Configurations;

internal sealed class FeedbackConfiguration : IEntityTypeConfiguration<Feedback>
{
    public void Configure(EntityTypeBuilder<Feedback> b)
    {
        b.ToTable("Feedback");
        b.HasKey(f => f.Id);
        b.Property(f => f.Id).ValueGeneratedNever();
        b.Property(f => f.UserName).HasMaxLength(200).IsRequired();
        b.Property(f => f.Message).HasMaxLength(Feedback.MaxMessageLength).IsRequired();

        // No foreign key: the message stays even when the account goes (the name is copied in).
        // The index serves "newest first" for the admin and "how many today" for the per-user limit.
        b.HasIndex(f => new { f.UserId, f.CreatedAt });
        b.HasIndex(f => f.CreatedAt);
    }
}
