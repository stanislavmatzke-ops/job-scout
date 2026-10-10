using JobScout.Application.Admin;
using JobScout.Application.Llm;
using JobScout.Application.Profiles;
using JobScout.Application.Users;
using JobScout.Core.Domain;
using JobScout.Tests.TestUtils;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Tests.Application.Admin;

/// <summary>
/// ADR-0017 from the use-case side: the user asks, the admin sees who costs what and approves or suspends.
/// </summary>
public sealed class AccessUseCasesTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 10, 8, 0, 0, TimeSpan.Zero);

    private readonly SqliteTestDatabase _database = new();
    private readonly FixedTimeProvider _time = new(T0);

    public void Dispose() => _database.Dispose();

    private Guid AddUser(string subject, AccessStatus access = AccessStatus.Pending, DateTimeOffset? createdAt = null)
    {
        using var db = _database.CreateContext();
        var user = new User("google", subject, $"{subject}@example.com", subject, createdAt ?? T0);
        if (access != AccessStatus.Pending) user.Approve(T0);
        if (access == AccessStatus.Suspended) user.Suspend("Kapacita", T0);
        db.Users.Add(user);
        db.SaveChanges();
        return user.Id;
    }

    private void AddCost(Guid userId, DateTimeOffset at, long microUsd)
    {
        using var db = _database.CreateContext();
        db.LlmCalls.Add(LlmCall.Success(userId, null, "Evaluation", "fake-model", 1000, 100, microUsd, 500, at));
        db.SaveChanges();
    }

    private User Stored(Guid id)
    {
        using var db = _database.CreateContext();
        return db.Users.Single(u => u.Id == id);
    }

    // ---------- the user asks ----------

    [Fact]
    public async Task Pending_user_requests_access()
    {
        var id = AddUser("jana");

        bool recorded;
        await using (var db = _database.CreateContext())
            recorded = await new RequestAccess(db, _time).ExecuteAsync(id, "Hledám práci.");

        Assert.True(recorded);
        Assert.Equal(("Hledám práci.", T0), (Stored(id).AccessRequestMessage, Stored(id).AccessRequestedAt!.Value));
    }

    [Fact]
    public async Task Approved_user_has_nothing_to_request()
    {
        var id = AddUser("jana", AccessStatus.Approved);

        await using var db = _database.CreateContext();
        Assert.False(await new RequestAccess(db, _time).ExecuteAsync(id, null));
    }

    // ---------- the admin decides ----------

    [Fact]
    public async Task Admin_approves_and_suspends()
    {
        var id = AddUser("jana");

        await using (var db = _database.CreateContext())
            Assert.True(await new ApproveUser(db, _time).ExecuteAsync(id));
        Assert.Equal(AccessStatus.Approved, Stored(id).Access);

        await using (var db = _database.CreateContext())
            Assert.True(await new SuspendUser(db, _time).ExecuteAsync(id, "Z kapacitních důvodů."));
        Assert.Equal((AccessStatus.Suspended, "Z kapacitních důvodů."), (Stored(id).Access, Stored(id).SuspendedReason));
    }

    [Fact]
    public async Task Unknown_user_is_not_found()
    {
        await using var db = _database.CreateContext();
        Assert.False(await new ApproveUser(db, _time).ExecuteAsync(Guid.NewGuid()));
        Assert.False(await new SuspendUser(db, _time).ExecuteAsync(Guid.NewGuid(), "Kapacita"));
    }

    [Fact]
    public async Task Suspension_without_reason_is_rejected()
    {
        var id = AddUser("jana", AccessStatus.Approved);

        await using var db = _database.CreateContext();
        await Assert.ThrowsAsync<ArgumentException>(() => new SuspendUser(db, _time).ExecuteAsync(id, " "));
    }

    // ---------- the overview ----------

    [Fact]
    public async Task Overview_shows_ai_cost_for_7_and_30_days()
    {
        var id = AddUser("jana", AccessStatus.Approved);
        AddCost(id, T0.AddDays(-2), 100_000);       // $0.10 – both windows
        AddCost(id, T0.AddDays(-20), 300_000);      // $0.30 – 30 days only
        AddCost(id, T0.AddDays(-40), 1_000_000);    // too old

        await using var db = _database.CreateContext();
        var row = Assert.Single(await new ListUsers(db, _time).ExecuteAsync());

        Assert.Equal((0.10m, 0.40m), (row.CostLast7DaysUsd, row.CostLast30DaysUsd));
        Assert.Equal(AccessStatus.Approved, row.Access);
        Assert.False(row.HasProfile);
    }

    [Fact]
    public async Task Waiting_requests_come_first_then_the_most_expensive()
    {
        var cheap = AddUser("cheap", AccessStatus.Approved);
        var expensive = AddUser("expensive", AccessStatus.Approved);
        var silent = AddUser("silent");                              // pending, never asked
        var asking = AddUser("asking");
        await using (var db = _database.CreateContext())
            await new RequestAccess(db, _time).ExecuteAsync(asking, "Prosím o přístup");
        AddCost(cheap, T0.AddDays(-1), 10_000);
        AddCost(expensive, T0.AddDays(-1), 900_000);

        await using var check = _database.CreateContext();
        var rows = await new ListUsers(check, _time).ExecuteAsync();

        Assert.Equal(new[] { asking, expensive, cheap, silent }, rows.Select(r => r.Id).ToArray());
        Assert.Equal("Prosím o přístup", rows[0].AccessRequestMessage);
    }

    // ---------- the Worker's personas ----------

    [Fact]
    public async Task Persona_created_by_the_worker_is_approved()
    {
        var llm = new FakeLlmClient(LlmJson.Serialize(TestProfiles.With(["Zedník"])));
        await using (var db = _database.CreateContext())
            await new AnalyzeProfile(db, new ProfileExtractor(llm), _time).ExecuteAsync(new ProfileInput(
                SignInUser.DevProvider, "zednik", "zednik", "cv.md", new LlmDocument("CV", "text/plain", "# Zedník"u8.ToArray()), null));

        await using var check = _database.CreateContext();
        Assert.Equal(AccessStatus.Approved, (await check.Users.SingleAsync()).Access);
    }
}
