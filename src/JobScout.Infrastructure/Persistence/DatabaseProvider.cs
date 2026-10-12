using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace JobScout.Infrastructure.Persistence;

/// <summary>
/// Which database (ADR-0018): SQLite locally and in tests (a file, nothing to install), PostgreSQL in Azure.
/// Chosen by Database:Provider – "Sqlite" (default) or "Postgres"; the connection string is ConnectionStrings:JobScout either way.
/// </summary>
public static class DatabaseProvider
{
    public const string Sqlite = "Sqlite";
    public const string Postgres = "Postgres";

    /// <summary>The assembly with the PostgreSQL migrations (the SQLite ones live next to the DbContext).</summary>
    public const string PostgresMigrationsAssembly = "JobScout.Migrations.Postgres";

    public static void Configure(DbContextOptionsBuilder options, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("JobScout")
            ?? throw new InvalidOperationException("Chybí ConnectionStrings:JobScout v konfiguraci.");
        Configure(options, config["Database:Provider"] ?? Sqlite, connectionString);
    }

    public static void Configure(DbContextOptionsBuilder options, string provider, string connectionString)
    {
        if (string.Equals(provider, Postgres, StringComparison.OrdinalIgnoreCase))
        {
            options.UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsAssembly(PostgresMigrationsAssembly)
                .EnableRetryOnFailure());           // a managed database in the cloud blinks now and then – retry transient errors
            return;
        }

        if (!string.Equals(provider, Sqlite, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Database:Provider musí být '{Sqlite}' nebo '{Postgres}', ne '{provider}'.");

        options.UseSqlite(connectionString);
    }
}
