using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobScout.Infrastructure.Persistence.Configurations;

internal sealed class LlmCallConfiguration : IEntityTypeConfiguration<LlmCall>
{
    public void Configure(EntityTypeBuilder<LlmCall> b)
    {
        b.HasKey(c => c.Id);
        b.Property(c => c.Id).ValueGeneratedNever();
        b.Property(c => c.Purpose).HasMaxLength(30).IsRequired();
        b.Property(c => c.Model).HasMaxLength(100).IsRequired();
        b.Property(c => c.Error).HasMaxLength(LlmCall.MaxErrorLength);
        b.Ignore(c => c.CostUsd);

        // Audit log: no foreign keys (written before a new user is saved; ADR-0016) – just indexes for reports
        b.HasIndex(c => new { c.UserId, c.At });
        b.HasIndex(c => c.ScoutRunId);
    }
}