using JobScout.Application.Abstractions;
using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace JobScout.Infrastructure.Persistence;

public sealed class JobScoutDbContext(DbContextOptions<JobScoutDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<JobOffer> JobOffers => Set<JobOffer>();
    public DbSet<JobMatch> JobMatches => Set<JobMatch>();
    public DbSet<SourceCategory> SourceCategories => Set<SourceCategory>(); 
    public DbSet<ScoutRun> ScoutRuns => Set<ScoutRun>(); 
    // Picks up every IEntityTypeConfiguration<T> in this assembly (Persistence/Configurations)
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(JobScoutDbContext).Assembly);

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Enums as readable text ("Applied", not 2) – survives reordering the enum, readable in the DB
        builder.Properties<MatchStage>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<MatchStatus>().HaveConversion<string>().HaveMaxLength(20);

        // SQLite stores DateTimeOffset as text and can't ORDER BY it → store as a sortable number.
        // PostgreSQL (Azure) has native timestamptz, so this applies to SQLite only.
        if (Database.IsSqlite())
        {
            builder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
            builder.Properties<DateTimeOffset?>().HaveConversion<DateTimeOffsetToBinaryConverter>();
        }
        
        builder.Properties<ScoutRunStatus>().HaveConversion<string>().HaveMaxLength(20);
    }
}