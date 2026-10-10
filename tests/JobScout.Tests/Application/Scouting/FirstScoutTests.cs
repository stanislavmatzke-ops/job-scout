using JobScout.Api.Scouting;
using JobScout.Application.Abstractions;
using JobScout.Application.Scouting;
using JobScout.Core.Domain;
using JobScout.Tests.TestUtils;

namespace JobScout.Tests.Application.Scouting;

/// <summary>
/// 6h – a new user doesn't wait for the morning: the first search starts right after the first CV,
/// only for an approved account, only once (afterwards the morning run searches), and never twice at the same time.
/// </summary>
public sealed class FirstScoutTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 10, 16, 0, 0, TimeSpan.Zero);

    private readonly SqliteTestDatabase _database = new();
    private readonly ScoutQueue _queue = new();

    public void Dispose() => _database.Dispose();

    private Guid AddUser(AccessStatus access = AccessStatus.Approved, bool withProfile = true, params ScoutRunStatus[] runs)
    {
        using var db = _database.CreateContext();
        var user = new User("google", Guid.NewGuid().ToString(), "", "Jana", T0);
        if (access != AccessStatus.Pending) user.Approve(T0);
        if (access == AccessStatus.Suspended) user.Suspend("Kapacita", T0);
        db.Users.Add(user);
        db.SaveChanges();
        if (withProfile)
            ProfileSeeder.Add(db, user.Id, T0);

        foreach (var status in runs)
        {
            var run = new ScoutRun(user.Id, T0);
            if (status == ScoutRunStatus.Succeeded) run.Succeed(10, 3, T0.AddMinutes(5));
            if (status == ScoutRunStatus.Failed) run.Fail("jobs.cz nedostupné", T0.AddMinutes(1));
            db.ScoutRuns.Add(run);
        }
        db.SaveChanges();
        return user.Id;
    }

    private async Task<bool> StartAsync(Guid userId)
    {
        await using var db = _database.CreateContext();
        return await new StartFirstScout(db, _queue).ExecuteAsync(userId);
    }

    [Fact]
    public async Task New_user_with_a_profile_is_searched_for_right_away()
    {
        var jana = AddUser();

        Assert.True(await StartAsync(jana));
        Assert.True(_queue.IsBusy(jana));
    }

    [Fact]
    public async Task Twice_in_a_row_is_queued_once()
    {
        var jana = AddUser();

        Assert.True(await StartAsync(jana));
        Assert.False(await StartAsync(jana));                 // a second upload while the first search waits / runs
    }

    [Fact]
    public async Task Somebody_already_searched_for_waits_for_the_morning()
    {
        var jana = AddUser(runs: ScoutRunStatus.Succeeded);
        var running = AddUser(runs: ScoutRunStatus.Running);  // the Worker is on it right now

        Assert.False(await StartAsync(jana));
        Assert.False(await StartAsync(running));
    }

    [Fact]
    public async Task A_failed_first_search_is_tried_again()
    {
        var jana = AddUser(runs: ScoutRunStatus.Failed);

        Assert.True(await StartAsync(jana));
    }

    [Theory]
    [InlineData(AccessStatus.Pending)]
    [InlineData(AccessStatus.Suspended)]
    public async Task No_search_without_approved_access(AccessStatus access)
    {
        var jana = AddUser(access);

        Assert.False(await StartAsync(jana));
        Assert.False(_queue.IsBusy(jana));
    }

    [Fact]
    public async Task No_profile_no_search()
    {
        Assert.False(await StartAsync(AddUser(withProfile: false)));
    }

    [Fact]
    public async Task Status_says_searching_and_when_the_last_search_finished()
    {
        var jana = AddUser(runs: [ScoutRunStatus.Failed, ScoutRunStatus.Succeeded]);
        _queue.TryEnqueue(jana);

        await using var db = _database.CreateContext();
        var status = await new GetScoutStatus(db, _queue).ExecuteAsync(jana);

        Assert.Equal((true, T0.AddMinutes(5)), (status.Searching, status.LastSearchAt));
    }

    [Fact]
    public async Task Never_searched_has_no_last_search()
    {
        var jana = AddUser();

        await using var db = _database.CreateContext();
        var status = await new GetScoutStatus(db, new DisabledScoutQueue()).ExecuteAsync(jana);

        Assert.Equal((false, (DateTimeOffset?)null), (status.Searching, status.LastSearchAt));
    }
}
