using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace JobScout.Infrastructure.Persistence;

/// <summary>
/// Used only by "dotnet ef" to create migrations – no need to start the whole Worker (API keys, browser…).
/// Default = SQLite (migrations in this project); "-- postgres" after the dotnet ef command = PostgreSQL
/// (migrations in JobScout.Migrations.Postgres). Creating a migration never connects to the database.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<JobScoutDbContext>
{
    public JobScoutDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<JobScoutDbContext>();
        if (args.Any(a => string.Equals(a, "postgres", StringComparison.OrdinalIgnoreCase)))
            DatabaseProvider.Configure(options, DatabaseProvider.Postgres, "Host=localhost;Database=jobscout_design_time");
        else
            DatabaseProvider.Configure(options, DatabaseProvider.Sqlite, "Data Source=design-time.db");
        return new JobScoutDbContext(options.Options);
    }
}
