using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using JobScout.Application.Jobs;
using JobScout.Application.Llm;
using JobScout.Infrastructure.Persistence;
using JobScout.Tests.TestUtils;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JobScout.Tests.Api;

/// <summary>
/// The real API in memory (TestServer) over its own in-memory SQLite database.
/// The database lives as long as <see cref="_keepAlive"/> is open; every factory = a fresh, migrated database.
/// No network: nothing here calls the AI or jobs.cz.
/// </summary>
public sealed class JobScoutApiFactory(
    bool devSignIn = true, string environment = "Development", IReadOnlyDictionary<string, string>? settings = null)
    : WebApplicationFactory<Program>
{
    private readonly string _connectionString = $"Data Source=api-tests-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private SqliteConnection? _keepAlive;
    private FakeLlmClient? _llm;

    /// <summary>What the fake AI answers, per purpose: (text, stop reason). Nothing prepared = the test fails loudly.</summary>
    internal Dictionary<LlmPurpose, (string Text, string StopReason)> AiAnswers { get; } = [];

    /// <summary>The AI the API talks to – no network, no tokens; Requests show what would have been paid for.</summary>
    internal FakeLlmClient Llm => _llm ??= new FakeLlmClient(request =>
        AiAnswers.TryGetValue(request.Purpose, out var answer)
            ? new LlmResponse(answer.Text, "fake-model", answer.StopReason, InputTokens: 100, OutputTokens: 50)
            : throw new InvalidOperationException($"No fake AI answer prepared for {request.Purpose}."));

    /// <summary>The job portal the API talks to.</summary>
    internal FakeJobSource JobsCz { get; } = new("jobs.cz");

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

        // No real Microsoft/Google sign-in in tests – even if your user secrets configure them
        builder.UseSetting("Auth:Microsoft:ClientId", "");
        builder.UseSetting("Auth:Google:ClientId", "");
        foreach (var (key, value) in settings ?? new Dictionary<string, string>())
            builder.UseSetting(key, value);

        // Real Claude and jobs.cz replaced by fakes – everything else is the real app
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ILlmClient>();
            services.AddSingleton<ILlmClient>(Llm);
            services.RemoveAll<IJobSource>();
            services.AddSingleton<IJobSource>(JobsCz);
        });
    }

    /// <summary>
    /// Client over https (the session cookie is Secure) that keeps cookies between requests, like a browser –
    /// and sends the X-CSRF header our Angular app sends with every call (5e).
    /// </summary>
    public HttpClient CreateBrowser(bool handleCookies = true, bool csrfHeader = true)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = handleCookies
        });
        if (csrfHeader)
            client.DefaultRequestHeaders.Add("X-CSRF", "1");
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _keepAlive?.Dispose();
    }
}
