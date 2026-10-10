using JobScout.Application.Users;
using JobScout.Core.Domain;
using JobScout.Tests.TestUtils;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Tests.Application.Users;

/// <summary>
/// Sign-in = find the user by provider + subject (never by e-mail), create on first visit,
/// refresh e-mail and name on later visits. Invitations come in phase 5e.
/// </summary>
public sealed class SignInUserTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 10, 8, 0, 0, TimeSpan.Zero);

    private readonly SqliteTestDatabase _database = new();
    private readonly FixedTimeProvider _time = new(T0);

    public void Dispose() => _database.Dispose();

    private async Task<User> SignInAsync(string provider, string subject, string email = "", string name = "Test")
    {
        await using var db = _database.CreateContext();
        return await new SignInUser(db, _time).ExecuteAsync(new SignInRequest(provider, subject, email, name));
    }

    [Fact]
    public async Task First_sign_in_creates_the_user()
    {
        var user = await SignInAsync("google", "g-123", "jana@example.com", "Jana");

        await using var db = _database.CreateContext();
        var stored = await db.Users.SingleAsync();
        Assert.Equal(user.Id, stored.Id);
        Assert.Equal(("google", "g-123", "jana@example.com", "Jana"), (stored.Provider, stored.Subject, stored.Email, stored.DisplayName));
        Assert.Equal(T0, stored.CreatedAt);
    }

    [Fact]
    public async Task Next_sign_in_returns_the_same_user()
    {
        var first = await SignInAsync("google", "g-123");
        _time.Now = T0.AddDays(1);
        var second = await SignInAsync("google", "g-123");

        Assert.Equal(first.Id, second.Id);
        await using var db = _database.CreateContext();
        Assert.Equal(1, await db.Users.CountAsync());
    }

    [Fact]
    public async Task Changed_email_and_name_at_the_provider_are_refreshed()
    {
        await SignInAsync("microsoft", "m-1", "old@example.com", "Old Name");
        await SignInAsync("microsoft", "m-1", "new@example.com", "New Name");

        await using var db = _database.CreateContext();
        var user = await db.Users.SingleAsync();
        Assert.Equal("new@example.com", user.Email);
        Assert.Equal("New Name", user.DisplayName);
    }

    [Fact]
    public async Task Same_subject_at_another_provider_is_another_user()
    {
        var google = await SignInAsync("google", "same-id");
        var microsoft = await SignInAsync("microsoft", "same-id");

        Assert.NotEqual(google.Id, microsoft.Id);
    }

    [Fact]
    public async Task Same_email_at_another_provider_is_not_merged()
    {
        // Identity is provider + subject – an e-mail can be re-used or spoofed, so it never identifies a user (ADR-0011)
        var google = await SignInAsync("google", "g-1", "jana@example.com");
        var microsoft = await SignInAsync("microsoft", "m-1", "jana@example.com");

        Assert.NotEqual(google.Id, microsoft.Id);
    }

    [Fact]
    public async Task Dev_persona_finds_the_user_the_worker_created()
    {
        // The Worker creates "dev:zednik" when it analyses the persona's CV – the API must sign in as the same user
        Guid workerUserId;
        await using (var db = _database.CreateContext())
        {
            var created = new User("dev", "zednik", "", "zednik", T0);
            db.Users.Add(created);
            await db.SaveChangesAsync();
            workerUserId = created.Id;
        }

        var signedIn = await SignInAsync("dev", "zednik", name: "zednik");

        Assert.Equal(workerUserId, signedIn.Id);
    }
}
