using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> b)
    {
        b.HasKey(i => i.Id);
        b.Property(i => i.Id).ValueGeneratedNever();
        b.Property(i => i.CodeHash).HasMaxLength(64).IsRequired();
        b.HasIndex(i => i.CodeHash).IsUnique();                // sign-in looks the invitation up by the hash
        b.Property(i => i.Note).HasMaxLength(200);
        b.HasOne<User>().WithMany().HasForeignKey(i => i.UsedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}
