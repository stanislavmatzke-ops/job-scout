using JobScout.Application.Users;
using JobScout.Core.Domain;
using JobScout.Tests.TestUtils;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Tests.Application.Users;

/// <summary>
/// Sign-in = find the user by provider + subject (never by e-mail). Anyone may sign in (ADR-0017):
/// a new identity is created as Pending and asks for access. Approved straight away: a valid invitation (ADR-0011),
/// a dev persona (ADR-0014), or an admin from configuration.
/// </summary>
public sealed class SignInUserTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 10, 8, 0, 0, TimeSpan.Zero);

    private readonly SqliteTestDatabase _database = new();
    private readonly FixedTimeProvider _time = new(T0);

    public void Dispose() => _database.Dispose();

    private async Task<SignInResult> SignInAsync(string provider, string subject, string email = "", string name = "Test",
        string? invite = null, bool isAdmin = false)
    {
        await using var db = _database.CreateContext();
        return await new SignInUser(db, _time).ExecuteAsync(new SignInRequest(provider, subject, email, name, invite, isAdmin));
    }

    private string Invite(DateTimeOffset? at = null)
    {
        using var db = _database.CreateContext();
        var (invitation, code) = Invitation.Create("test", at ?? T0);
        db.Invitations.Add(invitation);
        db.SaveChanges();
        return code;
    }

    private User StoredUser()
    {
        using var db = _database.CreateContext();
        return db.Users.Single();
    }

    // ---------- new people ----------

    [Fact]
    public async Task New_identity_signs_in_and_waits_for_approval()
    {
        var result = await SignInAsync("google", "g-123", "jana@example.com", "Jana");

        Assert.Equal(SignInStatus.SignedIn, result.Status);
        var user = StoredUser();
        Assert.Equal(("google", "g-123", "jana@example.com", "Jana"), (user.Provider, user.Subject, user.Email, user.DisplayName));
        Assert.Equal(AccessStatus.Pending, user.Access);
        Assert.Equal(T0, user.LastSignInAt);
    }

    [Fact]
    public async Task Valid_invitation_approves_straight_away_and_is_used_up()
    {
        var result = await SignInAsync("google", "g-123", invite: Invite());

        Assert.Equal(SignInStatus.SignedIn, result.Status);
        Assert.Equal(AccessStatus.Approved, StoredUser().Access);
        await using var db = _database.CreateContext();
        var invitation = await db.Invitations.SingleAsync();
        Assert.Equal(result.User!.Id, invitation.UsedByUserId);
    }

    [Fact]
    public async Task Used_or_unknown_invitation_is_reported_and_nobody_is_created()
    {
        var code = Invite();
        await SignInAsync("google", "g-1", invite: code);

        Assert.Equal(SignInStatus.InvitationInvalid, (await SignInAsync("microsoft", "m-9", invite: code)).Status);
        Assert.Equal(SignInStatus.InvitationInvalid, (await SignInAsync("microsoft", "m-9", invite: "made-up")).Status);
        await using var db = _database.CreateContext();
        Assert.Equal(1, await db.Users.CountAsync());
    }

    [Fact]
    public async Task Expired_invitation_is_invalid()
    {
        var code = Invite(T0 - Invitation.ValidFor - TimeSpan.FromMinutes(1));

        Assert.Equal(SignInStatus.InvitationInvalid, (await SignInAsync("google", "g-123", invite: code)).Status);
    }

    [Fact]
    public async Task Admin_from_configuration_is_approved()
    {
        await SignInAsync("microsoft", "m-admin", isAdmin: true);

        Assert.Equal(AccessStatus.Approved, StoredUser().Access);
    }

    [Fact]
    public async Task Dev_persona_is_approved()
    {
        await SignInAsync(SignInUser.DevProvider, "zednik", name: "zednik");

        Assert.Equal(AccessStatus.Approved, StoredUser().Access);
    }

    // ---------- known people ----------

    [Fact]
    public async Task Known_user_keeps_their_access_and_contact_is_refreshed()
    {
        var first = await SignInAsync("microsoft", "m-1", "old@example.com", "Old Name");
        _time.Now = T0.AddDays(30);

        var again = await SignInAsync("microsoft", "m-1", "new@example.com", "New Name");

        Assert.Equal(first.User!.Id, again.User!.Id);
        var user = StoredUser();
        Assert.Equal(("new@example.com", "New Name"), (user.Email, user.DisplayName));
        Assert.Equal(AccessStatus.Pending, user.Access);            // signing in again doesn't approve anybody
        Assert.Equal(T0.AddDays(30), user.LastSignInAt);
    }

    [Fact]
    public async Task Suspended_user_stays_suspended_after_sign_in()
    {
        await SignInAsync("google", "g-1", invite: Invite());
        await using (var db = _database.CreateContext())
        {
            (await db.Users.SingleAsync()).Suspend("Kapacita", T0);
            await db.SaveChangesAsync();
        }

        await SignInAsync("google", "g-1", invite: Invite());           // even with another invitation

        Assert.Equal(AccessStatus.Suspended, StoredUser().Access);
    }

    [Fact]
    public async Task Admin_who_was_pending_gets_approved_on_sign_in()
    {
        // e.g. the owner signed in before being added to Auth:Admins
        await SignInAsync("microsoft", "m-owner");

        await SignInAsync("microsoft", "m-owner", isAdmin: true);

        Assert.Equal(AccessStatus.Approved, StoredUser().Access);
    }

    [Fact]
    public async Task Same_email_at_another_provider_is_another_user()
    {
        var google = await SignInAsync("google", "g-1", "jana@example.com");
        var microsoft = await SignInAsync("microsoft", "m-1", "jana@example.com");

        Assert.NotEqual(google.User!.Id, microsoft.User!.Id);
    }

    [Fact]
    public async Task Dev_persona_finds_the_user_the_worker_created()
    {
        Guid workerUserId;
        await using (var db = _database.CreateContext())
        {
            var created = new User("dev", "zednik", "", "zednik", T0);
            db.Users.Add(created);
            await db.SaveChangesAsync();
            workerUserId = created.Id;
        }

        var signedIn = await SignInAsync("dev", "zednik", name: "zednik");

        Assert.Equal(workerUserId, signedIn.User!.Id);
    }
}
