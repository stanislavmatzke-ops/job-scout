using System.Net;
using System.Net.Http.Json;
using JobScout.Application.Llm;
using JobScout.Core.Domain;
using JobScout.Tests.TestUtils;

namespace JobScout.Tests.Api;

/// <summary>
/// ADR-0020 – "Try it without signing up": one shared fictional user. Visitors click through real jobs,
/// change statuses and notes – but never spend AI tokens or change the CV and the letters.
/// </summary>
public sealed class DemoEndpointsTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 12, 6, 0, 0, TimeSpan.Zero);

    private readonly JobScoutApiFactory _factory = new(devSignIn: false, settings: new Dictionary<string, string>
    {
        ["Demo:Enabled"] = "true",
        ["Auth:Admins:0"] = "demo:nobody",
    });

    public void Dispose() => _factory.Dispose();

    private sealed record Providers(bool DevSignIn, string? Demo);
    private sealed record Me(string DisplayName, string Access, bool IsDemo);

    private async Task<HttpClient> DemoAsync()
    {
        var browser = _factory.CreateBrowser();
        Assert.Equal(HttpStatusCode.NoContent, (await browser.PostAsync("/api/auth/demo", null)).StatusCode);
        return browser;
    }

    [Fact]
    public async Task The_login_page_offers_the_demo()
    {
        var providers = await _factory.CreateBrowser().GetFromJsonAsync<Providers>("/api/auth/providers");

        Assert.Equal((false, "Honza Testovací"), (providers!.DevSignIn, providers.Demo));
    }

    [Fact]
    public async Task Anyone_gets_in_as_the_same_approved_demo_user()
    {
        var me = await (await DemoAsync()).GetFromJsonAsync<Me>("/api/me");

        Assert.Equal(("Honza Testovací", "Approved", true), (me!.DisplayName, me.Access, me.IsDemo));
        Assert.Equal(HttpStatusCode.OK, (await (await DemoAsync()).GetAsync("/api/matches")).StatusCode);
    }

    [Fact]
    public async Task The_demo_clicks_but_spends_no_ai_and_changes_no_letters()
    {
        var browser = await DemoAsync();
        Guid matchId;
        await using (var db = _factory.CreateDbContext())
        {
            var demo = db.Users.Single(u => u.Provider == "demo");
            matchId = MatchSeeder.Add(db, demo.Id, ".NET vývojář", T0, MatchStage.Evaluated);
        }

        Assert.Equal(HttpStatusCode.NoContent,
            (await browser.PostAsJsonAsync($"/api/matches/{matchId}/status", new { status = "Applied" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await browser.PutAsJsonAsync($"/api/matches/{matchId}/notes", new { notes = "Zkouška" })).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await browser.PostAsync($"/api/matches/{matchId}/evaluate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.PostAsync($"/api/matches/{matchId}/letter", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await browser.PutAsJsonAsync($"/api/matches/{matchId}/letter", new { text = "Můj dopis" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await browser.PutAsJsonAsync("/api/profile/notes", new { notes = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.PostAsync("/api/scout/first", null)).StatusCode);
        Assert.Equal(0, _factory.Llm.Calls);
    }

    [Fact]
    public async Task Switched_off_the_demo_does_not_exist()
    {
        using var factory = new JobScoutApiFactory();

        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateBrowser().PostAsync("/api/auth/demo", null)).StatusCode);
    }

    [Fact]
    public async Task Admin_prepares_the_demo_profile_with_one_ai_analysis()
    {
        using var factory = new JobScoutApiFactory(settings: new Dictionary<string, string>
        {
            ["Demo:Enabled"] = "true",
            ["Auth:Admins:0"] = "dev:boss",
        });
        factory.AiAnswers[LlmPurpose.Profile] = (LlmJson.Serialize(TestProfiles.With([".NET vývojář"])), "end_turn");
        var boss = factory.CreateBrowser();
        await boss.PostAsync("/api/auth/dev/boss", null);

        var prepare = await boss.PostAsync("/api/admin/demo/prepare", null);

        Assert.Equal(HttpStatusCode.OK, prepare.StatusCode);
        Assert.Equal(1, factory.Llm.Calls);                       // the CV + notes analysed once
        await using var db = factory.CreateDbContext();
        var demo = db.Users.Single(u => u.Provider == "demo");
        Assert.Equal(AccessStatus.Approved, demo.Access);
        Assert.True(db.UserProfiles.Any(p => p.UserId == demo.Id));
    }
}
