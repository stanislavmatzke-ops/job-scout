using JobScout.Application.Abstractions;
using JobScout.Core.Domain;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace JobScout.Infrastructure.Persistence;

public sealed class JobScoutDbContext(DbContextOptions<JobScoutDbContext> options)
    : DbContext(options), IAppDbContext, IDataProtectionKeyContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<JobOffer> JobOffers => Set<JobOffer>();
    public DbSet<JobMatch> JobMatches => Set<JobMatch>();
    public DbSet<SourceCategory> SourceCategories => Set<SourceCategory>();
    public DbSet<ScoutRun> ScoutRuns => Set<ScoutRun>();
    public DbSet<LlmCall> LlmCalls => Set<LlmCall>();
    public DbSet<StoredCv> StoredCvs => Set<StoredCv>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<Feedback> Feedback => Set<Feedback>();

    /// <summary>
    /// The keys that encrypt the session cookie and the sign-in round trip (ASP.NET Core Data Protection).
    /// In a container the file system is thrown away on every restart – kept here, a restart doesn't sign everyone out.
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    // Picks up every IEntityTypeConfiguration<T> in this assembly (Persistence/Configurations)
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(JobScoutDbContext).Assembly);

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Enums as readable text ("Applied", not 2) – survives reordering the enum, readable in the DB
        builder.Properties<MatchStage>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<MatchStatus>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<GrammaticalGender>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<AccessStatus>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<FeedbackKind>().HaveConversion<string>().HaveMaxLength(20);

        // SQLite stores DateTimeOffset as text and can't ORDER BY it → store as a sortable number.
        // PostgreSQL (Azure) has native timestamptz, so this applies to SQLite only.
        if (Database.IsSqlite())
        {
            builder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
            builder.Properties<DateTimeOffset?>().HaveConversion<DateTimeOffsetToBinaryConverter>();
        }
        else if (Database.IsNpgsql())
        {
            // timestamptz accepts only UTC (offset 0) – a time with +02:00 would throw on save. Store everything as UTC.
            builder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>();
            builder.Properties<DateTimeOffset?>().HaveConversion<UtcDateTimeOffsetConverter>();
        }

        builder.Properties<ScoutRunStatus>().HaveConversion<string>().HaveMaxLength(20);
    }
}

/// <summary>Any offset in, UTC out (PostgreSQL timestamptz); reads come back as UTC.</summary>
public sealed class UtcDateTimeOffsetConverter() : ValueConverter<DateTimeOffset, DateTimeOffset>(
    v => v.ToUniversalTime(),
    v => v.ToUniversalTime());
