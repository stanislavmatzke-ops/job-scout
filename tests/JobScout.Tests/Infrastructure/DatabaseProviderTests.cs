using JobScout.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Tests.Infrastructure;

/// <summary>
/// ADR-0018 – one DbContext, two databases: SQLite (local, tests) and PostgreSQL (Azure).
/// "Pending model changes" = somebody changed an entity and forgot the migration for one of the providers –
/// production would then start with a schema that doesn't match the code. No database is needed for this check.
/// </summary>
public sealed class DatabaseProviderTests
{
    private static JobScoutDbContext Context(string provider, string connectionString)
    {
        var options = new DbContextOptionsBuilder<JobScoutDbContext>();
        DatabaseProvider.Configure(options, provider, connectionString);
        return new JobScoutDbContext(options.Options);
    }

    [Fact]
    public void Sqlite_migrations_match_the_model()
    {
        using var db = Context(DatabaseProvider.Sqlite, "Data Source=:memory:");

        Assert.False(db.Database.HasPendingModelChanges(),
            "SQLite: dotnet ef migrations add <Name> -p src/JobScout.Infrastructure -s src/JobScout.Infrastructure -o Persistence/Migrations");
    }

    [Fact]
    public void Postgres_migrations_match_the_model()
    {
        using var db = Context(DatabaseProvider.Postgres, "Host=localhost;Database=never_connected");

        Assert.False(db.Database.HasPendingModelChanges(),
            "PostgreSQL: dotnet ef migrations add <Name> -p src/JobScout.Migrations.Postgres -s src/JobScout.Migrations.Postgres -o Migrations -- postgres");
    }

    [Fact]
    public void Postgres_stores_times_as_utc()
    {
        using var db = Context(DatabaseProvider.Postgres, "Host=localhost;Database=never_connected");
        var converter = db.Model.GetEntityTypes()
            .SelectMany(e => e.GetProperties())
            .First(p => p.ClrType == typeof(DateTimeOffset))
            .GetValueConverter();

        var stored = (DateTimeOffset)converter!.ConvertToProvider(new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.FromHours(2)))!;

        Assert.Equal(TimeSpan.Zero, stored.Offset);
        Assert.Equal(6, stored.Hour);
    }

    [Fact]
    public void Unknown_provider_is_a_clear_error()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DatabaseProvider.Configure(new DbContextOptionsBuilder<JobScoutDbContext>(), "MySql", "x"));

        Assert.Contains("Database:Provider", ex.Message);
    }
}
