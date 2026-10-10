using System.Net;
using System.Net.Http.Json;
using JobScout.Core.Domain;
using JobScout.Tests.TestUtils;

namespace JobScout.Tests.Api;

/// <summary>
/// ADR-0017 over HTTP: Pending sees only "who am I" and "ask for access"; Suspended reads but can't spend money on AI;
/// the admin (from configuration) lists users with costs, approves and suspends.
/// </summary>
public sealed class AccessEndpointsTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 10, 6, 0, 0, TimeSpan.Zero);

    private readonly JobScoutApiFactory _factory = new(settings: new Dictionary<string, string>
    {
        ["Auth:Admins:0"] = "dev:boss",
    });

    public void Dispose() => _factory.Dispose();

    private sealed record Me(Guid Id, string Access, string? SuspendedReason, DateTimeOffset? AccessRequestedAt,
        bool IsAdmin, string Identity);
    private sealed record CreatedInvitation(Guid Id, string Code, DateTimeOffset ExpiresAt);
    private sealed record InvitationRow(Guid Id, string Note, DateTimeOffset? UsedAt, string? UsedBy, bool IsUsable);
    private sealed record UserRow(Guid Id, string DisplayName, string Access, decimal CostLast30DaysUsd, string? AccessRequestMessage);

    /// <summary>A user in the given state who then signs in through the dev sign-in (which doesn't change an existing user).</summary>
    private async Task<(HttpClient Browser, Guid UserId)> SignInAsAsync(string persona, AccessStatus access)
    {
        _factory.CreateBrowser();                                   // start the app → migrated database
        Guid id;
        await using (var db = _factory.CreateDbContext())
        {
            var user = new User("dev", persona, "", persona, T0);  // new users start Pending
            if (access != AccessStatus.Pending) user.Approve(T0);
            if (access == AccessStatus.Suspended) user.Suspend("Z kapacitních důvodů je účet pozastaven.", T0);
            db.Users.Add(user);
            await db.SaveChangesAsync();
            id = user.Id;
        }

        var browser = _factory.CreateBrowser();
        await browser.PostAsync($"/api/auth/dev/{persona}", null);
        return (browser, id);
    }

    private async Task<HttpClient> AdminAsync()
    {
        var admin = _factory.CreateBrowser();
        await admin.PostAsync("/api/auth/dev/boss", null);
        return admin;
    }

    // ---------- pending ----------

    [Fact]
    public async Task Pending_user_sees_who_they_are_but_nothing_else()
    {
        var (browser, _) = await SignInAsAsync("pete", AccessStatus.Pending);

        var me = await browser.GetFromJsonAsync<Me>("/api/me");

        Assert.Equal(("Pending", false, "dev:pete"), (me!.Access, me.IsAdmin, me.Identity));
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.GetAsync("/api/matches")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.GetAsync("/api/profile")).StatusCode);
    }

    [Fact]
    public async Task Pending_user_asks_for_access()
    {
        var (browser, _) = await SignInAsAsync("pete", AccessStatus.Pending);

        var response = await browser.PostAsJsonAsync("/api/access/request", new { message = "Hledám práci jako zedník." });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.NotNull((await browser.GetFromJsonAsync<Me>("/api/me"))!.AccessRequestedAt);
    }

    [Fact]
    public async Task Approved_user_asking_again_is_409()
    {
        var (browser, _) = await SignInAsAsync("pete", AccessStatus.Approved);

        var response = await browser.PostAsJsonAsync("/api/access/request", new { message = "znovu" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ---------- suspended ----------

    [Fact]
    public async Task Suspended_user_reads_but_cannot_spend_ai_tokens()
    {
        var (browser, userId) = await SignInAsAsync("pete", AccessStatus.Suspended);
        Guid matchId;
        await using (var db = _factory.CreateDbContext())
            matchId = MatchSeeder.Add(db, userId, "Zedník", T0, MatchStage.Shortlisted);

        var me = await browser.GetFromJsonAsync<Me>("/api/me");
        Assert.Equal(("Suspended", "Z kapacitních důvodů je účet pozastaven."), (me!.Access, me.SuspendedReason));

        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/matches")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync($"/api/matches/{matchId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await browser.PostAsJsonAsync($"/api/matches/{matchId}/status", new { status = "Dismissed" })).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await browser.PostAsync($"/api/matches/{matchId}/evaluate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.PostAsync($"/api/matches/{matchId}/letter", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await browser.PutAsJsonAsync("/api/profile/notes", new { notes = "x" })).StatusCode);
        Assert.Equal(0, _factory.Llm.Calls);
    }

    // ---------- admin ----------

    [Fact]
    public async Task Admin_is_recognised_from_configuration()
    {
        var me = await (await AdminAsync()).GetFromJsonAsync<Me>("/api/me");

        Assert.Equal(("Approved", true), (me!.Access, me.IsAdmin));
    }

    [Fact]
    public async Task Only_the_admin_sees_the_users()
    {
        var (normal, _) = await SignInAsAsync("pete", AccessStatus.Approved);

        Assert.Equal(HttpStatusCode.Forbidden, (await normal.GetAsync("/api/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateBrowser().GetAsync("/api/admin/users")).StatusCode);
    }

    [Fact]
    public async Task Admin_sees_requests_with_costs_and_approves()
    {
        var (pete, peteId) = await SignInAsAsync("pete", AccessStatus.Pending);
        await pete.PostAsJsonAsync("/api/access/request", new { message = "Prosím." });
        await using (var db = _factory.CreateDbContext())
        {
            db.LlmCalls.Add(LlmCall.Success(peteId, null, "Profile", "fake-model", 1000, 100, 25_000, 400, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }
        var admin = await AdminAsync();

        var users = await admin.GetFromJsonAsync<List<UserRow>>("/api/admin/users");
        var row = Assert.Single(users!, u => u.Id == peteId);
        Assert.Equal(("Pending", 0.025m, "Prosím."), (row.Access, row.CostLast30DaysUsd, row.AccessRequestMessage));

        var approve = await admin.PostAsync($"/api/admin/users/{peteId}/approve", null);

        Assert.Equal(HttpStatusCode.NoContent, approve.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await pete.GetAsync("/api/matches")).StatusCode);   // the same session, no new sign-in
    }

    [Fact]
    public async Task Admin_suspends_with_a_reason()
    {
        var (pete, peteId) = await SignInAsAsync("pete", AccessStatus.Approved);
        var admin = await AdminAsync();

        var noReason = await admin.PostAsJsonAsync($"/api/admin/users/{peteId}/suspend", new { reason = "" });
        var suspend = await admin.PostAsJsonAsync($"/api/admin/users/{peteId}/suspend",
            new { reason = "Z kapacitních důvodů je účet pozastaven." });

        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, suspend.StatusCode);
        Assert.Equal("Suspended", (await pete.GetFromJsonAsync<Me>("/api/me"))!.Access);
    }

    [Fact]
    public async Task Admin_action_on_unknown_user_is_404()
    {
        var admin = await AdminAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"/api/admin/users/{Guid.NewGuid()}/approve", null)).StatusCode);
    }

    // ---------- invitations (6e) ----------

    [Fact]
    public async Task Admin_creates_an_invitation_and_sees_who_used_it()
    {
        var admin = await AdminAsync();

        var created = await (await admin.PostAsJsonAsync("/api/admin/invitations", new { note = "  Petr z HR  " }))
            .Content.ReadFromJsonAsync<CreatedInvitation>();
        Assert.False(string.IsNullOrWhiteSpace(created!.Code));

        var before = Assert.Single(await admin.GetFromJsonAsync<List<InvitationRow>>("/api/admin/invitations") ?? []);
        Assert.Equal(("Petr z HR", true, (string?)null), (before.Note, before.IsUsable, before.UsedBy));

        await using (var db = _factory.CreateDbContext())       // what the sign-in does with a valid code (SignInUserTests)
        {
            var user = new User("google", "petr-sub", "petr@example.com", "Petr", T0);
            db.Users.Add(user);
            var invitation = db.Invitations.Single(i => i.Id == created.Id);
            invitation.Use(user.Id, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        var after = Assert.Single(await admin.GetFromJsonAsync<List<InvitationRow>>("/api/admin/invitations") ?? []);
        Assert.Equal((false, "Petr"), (after.IsUsable, after.UsedBy));
        Assert.NotNull(after.UsedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Invitation_without_a_note_is_400(string note)
    {
        var admin = await AdminAsync();

        var response = await admin.PostAsJsonAsync("/api/admin/invitations", new { note });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Only_the_admin_creates_invitations()
    {
        var (normal, _) = await SignInAsAsync("pete", AccessStatus.Approved);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await normal.PostAsJsonAsync("/api/admin/invitations", new { note = "kamarád" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await normal.GetAsync("/api/admin/invitations")).StatusCode);
    }
}
