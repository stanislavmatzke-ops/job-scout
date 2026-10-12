using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobScout.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.HasKey(u => u.Id);
        b.Property(u => u.Id).ValueGeneratedNever();   // we create Guid v7 ourselves

        b.Property(u => u.Provider).HasMaxLength(20).IsRequired();
        b.Property(u => u.Subject).HasMaxLength(200).IsRequired();
        b.Property(u => u.Email).HasMaxLength(320);    // max length of an e-mail address
        b.Property(u => u.DisplayName).HasMaxLength(200);
        b.Property(u => u.AccessRequestMessage).HasMaxLength(User.MaxRequestMessageLength);
        b.Property(u => u.SuspendedReason).HasMaxLength(User.MaxSuspendReasonLength);
        b.Property(u => u.RejectedReason).HasMaxLength(User.MaxSuspendReasonLength);

        // Identity = provider + subject; this is how we find the user on every sign-in
        b.HasIndex(u => new { u.Provider, u.Subject }).IsUnique();
    }
}
