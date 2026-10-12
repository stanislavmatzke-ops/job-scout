using JobScout.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Design;

namespace JobScout.Migrations.Postgres;

/// <summary>
/// "dotnet ef" looks for the DbContext (or its factory) in the startup and the target project – this project has
/// neither, the context lives in Infrastructure. This factory points it there, always with PostgreSQL.
/// </summary>
public sealed class PostgresDesignTimeFactory : IDesignTimeDbContextFactory<JobScoutDbContext>
{
    public JobScoutDbContext CreateDbContext(string[] args) =>
        new DesignTimeDbContextFactory().CreateDbContext(["postgres"]);
}
