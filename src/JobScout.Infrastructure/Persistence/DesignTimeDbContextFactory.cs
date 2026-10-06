using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace JobScout.Infrastructure.Persistence;

/// <summary>Used only by "dotnet ef" to create migrations – no need to start the whole Worker (API keys, browser…).</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<JobScoutDbContext>
{
    public JobScoutDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<JobScoutDbContext>().UseSqlite("Data Source=design-time.db").Options);
}