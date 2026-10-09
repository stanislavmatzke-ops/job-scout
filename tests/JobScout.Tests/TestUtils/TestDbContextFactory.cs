using JobScout.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Tests.TestUtils;

/// <summary>IDbContextFactory over the in-memory test database – like AddDbContextFactory in the app.</summary>
internal sealed class TestDbContextFactory(SqliteTestDatabase database, bool fail = false) : IDbContextFactory<JobScoutDbContext>
{
    public JobScoutDbContext CreateDbContext() =>
        fail ? throw new InvalidOperationException("Databáze nedostupná") : database.CreateContext();
}
