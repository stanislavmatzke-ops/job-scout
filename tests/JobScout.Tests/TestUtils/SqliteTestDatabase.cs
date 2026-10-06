using JobScout.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Tests.TestUtils;

/// <summary>
/// Real SQLite database in memory – created by our migrations, gone after the test.
/// The connection must stay open: an in-memory SQLite database lives only as long as its connection.
/// Each test gets its own instance → tests don't see each other's data.
/// </summary>
internal sealed class SqliteTestDatabase : IDisposable
{
    public SqliteConnection Connection { get; } = new("DataSource=:memory:");

    public SqliteTestDatabase()
    {
        Connection.Open();
        using var db = CreateContext();
        db.Database.Migrate();   // the same migrations as the real app – tests check them too
    }

    /// <summary>A fresh context = like a new request / new morning run. Nothing cached from before.</summary>
    public JobScoutDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<JobScoutDbContext>().UseSqlite(Connection).Options);

    /// <summary>Raw SQL value – to check what is really stored in the table.</summary>
    public object? Scalar(string sql)
    {
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    public void Dispose() => Connection.Dispose();
}
