using JobScout.Application.Users;
using JobScout.Core.Domain;
using JobScout.Tests.TestUtils;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Tests.Application.Users;

/// <summary>
/// Sign-in = find the user by provider + subject (never by e-mail). A known user always gets in.
/// A NEW Microsoft/Google identity needs a valid invitation (ADR-0011); dev personas don't (dev sign-in exists only outside Production).
/// </summary>
public sealed class SignInUserTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 10, 8, 0, 0, TimeSpan.Zero);

    private readonly SqliteTestDatabase _database = new();
    private readonly FixedTimeProvider _time = new(T0);

    public void Dispose() => _database.Dispose();

    private async Task<SignInResult> SignInAsync(string provider, string subject, string email = "", string name = "Test",
        string? invite = null)
    {
        await using var db = _database.CreateContext();
        return await new SignInUser(db, _time).ExecuteAsync(new SignInRequest(provider, subject, email, name, invite));
    }

    /// <summary>Creates an invitation and returns its code (what the invited person gets in the link).</summary>
    private string Invite(DateTimeOffset? at = null)
    {
        using var db = _database.CreateContext();
        var (invitation, code) = Invitation.Create("test", at ?? T0);
        db.Invitations.Add(invitation);
        db.SaveChanges();
        return code;
    }

    private int UserCount()
    {
        using var db = _database.CreateContext();
        return db.Users.Count();
    }

    // ---------- new people need an invitation ----------

    [Fact]
    public async Task New_identity_without_invitation_is_turned_away()
    {
        var result = await SignInAsync("google", "g-123", "jana@example.com");

        Assert.Equal(SignInStatus.InvitationRequired, result.Status);
        Assert.Null(result.User);
        Assert.Equal(0, UserCount());
    }

    [Fact]
    public async Task Valid_invitation_creates_the_user_and_is_used_up()
    {
        var code = Invite();

        var result = await SignInAsync("google", "g-123", "jana@example.com", "Jana", invite: code);

        Assert.Equal(SignInStatus.SignedIn, result.Status);
        await using var db = _database.CreateContext();
        var user = await db.Users.SingleAsync();
        Assert.Equal(("google", "g-123", "jana@example.com", "Jana"), (user.Provider, user.Subject, user.Email, user.DisplayName));
        Assert.Equal(result.User!.Id, user.Id);
        var invitation = await db.Invitations.SingleAsync();
        Assert.Equal(user.Id, invitation.UsedByUserId);
        Assert.Equal(T0, invitation.UsedAt);
    }

    [Fact]
    public async Task Invitation_works_only_once()
    {
        var code = Invite();
        await SignInAsync("google", "g-123", invite: code);

        var second = await SignInAsync("microsoft", "m-999", invite: code);

        Assert.Equal(SignInStatus.InvitationInvalid, second.Status);
        Assert.Equal(1, UserCount());
    }

    [Fact]
    public async Task Expired_invitation_is_invalid()
    {
        var code = Invite(T0 - Invitation.ValidFor - TimeSpan.FromMinutes(1));

        Assert.Equal(SignInStatus.InvitationInvalid, (await SignInAsync("google", "g-123", invite: code)).Status);
        Assert.Equal(0, UserCount());
    }

    [Fact]
    public async Task Unknown_code_is_invalid()
    {
        Assert.Equal(SignInStatus.InvitationInvalid, (await SignInAsync("google", "g-123", invite: "made-up")).Status);
    }

    // ---------- known people ----------

    [Fact]
    public async Task Known_user_gets_in_without_invitation_and_contact_is_refreshed()
    {
        var first = await SignInAsync("microsoft", "m-1", "old@example.com", "Old Name", invite: Invite());
        _time.Now = T0.AddDays(30);

        var again = await SignInAsync("microsoft", "m-1", "new@example.com", "New Name");

        Assert.Equal(SignInStatus.SignedIn, again.Status);
        Assert.Equal(first.User!.Id, again.User!.Id);
        await using var db = _database.CreateContext();
        var user = await db.Users.SingleAsync();
        Assert.Equal(("new@example.com", "New Name"), (user.Email, user.DisplayName));
    }

    [Fact]
    public async Task Known_user_does_not_use_up_an_invitation_by_accident()
    {
        await SignInAsync("google", "g-1", invite: Invite());
        var spare = Invite();

        await SignInAsync("google", "g-1", invite: spare);

        Assert.Equal(SignInStatus.SignedIn, (await SignInAsync("google", "g-2", invite: spare)).Status);
    }

    [Fact]
    public async Task Same_email_at_another_provider_is_another_user()
    {
        // Identity is provider + subject – an e-mail can be re-used or spoofed, so it never identifies a user (ADR-0011)
        var google = await SignInAsync("google", "g-1", "jana@example.com", invite: Invite());
        var microsoft = await SignInAsync("microsoft", "m-1", "jana@example.com");

        Assert.Equal(SignInStatus.InvitationRequired, microsoft.Status);
        Assert.NotNull(google.User);
    }

    // ---------- dev personas ----------

    [Fact]
    public async Task Dev_persona_needs_no_invitation()
    {
        var result = await SignInAsync(SignInUser.DevProvider, "zednik", name: "zednik");

        Assert.Equal(SignInStatus.SignedIn, result.Status);
        Assert.Equal(1, UserCount());
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
