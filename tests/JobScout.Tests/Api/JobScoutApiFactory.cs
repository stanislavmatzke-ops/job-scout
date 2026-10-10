using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using JobScout.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Tests.Api;

/// <summary>
/// The real API in memory (TestServer) over its own in-memory SQLite database.
/// The database lives as long as <see cref="_keepAlive"/> is open; every factory = a fresh, migrated database.
/// No network: nothing here calls the AI or jobs.cz.
/// </summary>
public sealed class JobScoutApiFactory(bool devSignIn = true, string environment = "Development")
    : WebApplicationFactory<Program>
{
    private readonly string _connectionString = $"Data Source=api-tests-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private SqliteConnection? _keepAlive;

    public string ConnectionString => _connectionString;

    /// <summary>Direct access to the API's database – to seed data. Start the app first (CreateBrowser) so it is migrated.</summary>
    public JobScoutDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<JobScoutDbContext>().UseSqlite(_connectionString).Options);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _keepAlive ??= new SqliteConnection(_connectionString);
        _keepAlive.Open();

        builder.UseEnvironment(environment);
        builder.UseSetting("ConnectionStrings:JobScout", _connectionString);
        builder.UseSetting("Auth:DevSignIn:Enabled", devSignIn ? "true" : "false");
    }

    /// <summary>Client over https (the session cookie is Secure) that keeps cookies between requests, like a browser.</summary>
    public HttpClient CreateBrowser(bool handleCookies = true) =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = handleCookies
        });

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _keepAlive?.Dispose();
    }
}
