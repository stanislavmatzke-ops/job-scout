using System.Net;
using System.Net.Http.Json;
using JobScout.Core.Domain;
using JobScout.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Tests.Api;

/// <summary>
/// BFF sign-in (ADR-0011) and development sign-in for test personas (ADR-0014):
/// the session is an HttpOnly cookie, the API answers 401 (never a redirect) and dev sign-in is impossible in Production.
/// </summary>
public sealed class AuthEndpointsTests : IDisposable
{
    private readonly JobScoutApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private sealed record Me(Guid Id, string Provider, string DisplayName, string Email);

    // ---------- public / anonymous ----------

    [Fact]
    public async Task Health_is_public()
    {
        var response = await _factory.CreateBrowser().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Me_without_sign_in_is_401_not_a_redirect()
    {
        var response = await _factory.CreateBrowser().GetAsync("/api/me");

        // An SPA needs a status code it can react to – a 302 to a login page would be swallowed by fetch()
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------- dev sign-in ----------

    [Fact]
    public async Task Dev_sign_in_then_me_returns_the_persona()
    {
        var browser = _factory.CreateBrowser();

        var signIn = await browser.PostAsync("/api/auth/dev/zednik", null);
        var me = await browser.GetFromJsonAsync<Me>("/api/me");

        Assert.Equal(HttpStatusCode.NoContent, signIn.StatusCode);
        Assert.NotNull(me);
        Assert.Equal("dev", me.Provider);
        Assert.Equal("zednik", me.DisplayName);
        Assert.NotEqual(Guid.Empty, me.Id);
    }

    [Fact]
    public async Task Dev_sign_in_uses_the_user_the_worker_created()
    {
        var browser = _factory.CreateBrowser();          // starts the app → database migrated

        Guid workerUserId;
        await using (var db = new JobScoutDbContext(new DbContextOptionsBuilder<JobScoutDbContext>()
                         .UseSqlite(_factory.ConnectionString).Options))
        {
            var user = new User("dev", "autolakyrnik", "", "autolakyrnik", DateTimeOffset.UtcNow);
            db.Users.Add(user);
            await db.SaveChangesAsync();
            workerUserId = user.Id;
        }

        await browser.PostAsync("/api/auth/dev/autolakyrnik", null);
        var me = await browser.GetFromJsonAsync<Me>("/api/me");

        Assert.Equal(workerUserId, me!.Id);
    }

    [Fact]
    public async Task Session_cookie_is_http_only_secure_and_same_site()
    {
        var response = await _factory.CreateBrowser(handleCookies: false).PostAsync("/api/auth/dev/zednik", null);

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("__Host-jobscout="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Zednik")]            // upper case
    [InlineData("zed_nik")]           // underscore
    [InlineData("a-very-long-persona-name-that-nobody-needs")]
    public async Task Invalid_persona_name_is_400(string persona)
    {
        var response = await _factory.CreateBrowser().PostAsync($"/api/auth/dev/{persona}", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Logout_ends_the_session()
    {
        var browser = _factory.CreateBrowser();
        await browser.PostAsync("/api/auth/dev/zednik", null);

        var logout = await browser.PostAsync("/api/auth/logout", null);
        var me = await browser.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    // ---------- the switch ----------

    [Fact]
    public async Task Dev_sign_in_does_not_exist_when_disabled()
    {
        using var factory = new JobScoutApiFactory(devSignIn: false);

        var response = await factory.CreateBrowser().PostAsync("/api/auth/dev/zednik", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void Dev_sign_in_enabled_in_Production_stops_the_start()
    {
        using var factory = new JobScoutApiFactory(devSignIn: true, environment: "Production");

        var ex = Record.Exception(() => factory.CreateBrowser());

        Assert.NotNull(ex);
        Assert.Contains("DevSignIn", ex.ToString());
    }
}
