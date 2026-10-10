using JobScout.Application.Jobs;
using JobScout.Core.Domain;
using JobScout.Tests.TestUtils;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Tests.Application.Jobs;

/// <summary>Run journal: start → window from the last successful run → succeed / fail, all persisted.</summary>
public sealed class ScoutRunJournalTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 8, 6, 0, 0, TimeSpan.Zero);

    private readonly SqliteTestDatabase _database = new();
    private readonly FixedTimeProvider _time = new(T0);
    private readonly Guid _userId;

    public ScoutRunJournalTests()
    {
        using var db = _database.CreateContext();
        var user = new User("dev", "zednik", "", "zednik", T0);
        db.Users.Add(user);
        db.SaveChanges();
        _userId = user.Id;
    }

    public void Dispose() => _database.Dispose();

    private async Task<StartedRun> StartAsync()
    {
        await using var db = _database.CreateContext();
        return await new ScoutRunJournal(db, _time).StartAsync(_userId);
    }

    private async Task SucceedAsync(Guid runId, int found = 9, int newForUser = 9)
    {
        await using var db = _database.CreateContext();
        var run = await db.ScoutRuns.SingleAsync(r => r.Id == runId);
        await new ScoutRunJournal(db, _time).SucceedAsync(run, found, newForUser);
    }

    private async Task FailAsync(Guid runId, Exception error)
    {
        await using var db = _database.CreateContext();
        var run = await db.ScoutRuns.SingleAsync(r => r.Id == runId);
        await new ScoutRunJournal(db, _time).FailAsync(run, error);
    }

    private async Task<ScoutRun> LoadAsync(Guid runId)
    {
        await using var db = _database.CreateContext();
        return await db.ScoutRuns.SingleAsync(r => r.Id == runId);
    }

    [Fact]
    public async Task First_start_is_stored_as_running_with_a_three_day_window()
    {
        var started = await StartAsync();

        Assert.Equal(JobAge.ThreeDays, started.Window.MaxAge);
        var run = await LoadAsync(started.Run.Id);
        Assert.Equal(ScoutRunStatus.Running, run.Status);
        Assert.Equal(T0, run.StartedAt);
    }

    [Fact]
    public async Task Next_day_after_a_success_searches_24_hours()
    {
        var first = await StartAsync();
        await SucceedAsync(first.Run.Id);

        _time.Now = T0.AddDays(1).AddMinutes(-30);
        var second = await StartAsync();

        Assert.Equal(JobAge.Day, second.Window.MaxAge);
    }

    [Fact]
    public async Task Failed_run_does_not_count_so_the_next_run_covers_the_missed_day()
    {
        var monday = await StartAsync();
        await SucceedAsync(monday.Run.Id);

        _time.Now = T0.AddDays(1);
        var tuesday = await StartAsync();
        await FailAsync(tuesday.Run.Id, new HttpRequestException("503"));

        _time.Now = T0.AddDays(2);
        var wednesday = await StartAsync();

        Assert.Equal(JobAge.ThreeDays, wednesday.Window.MaxAge);   // Tuesday's ads are not lost
    }

    [Fact]
    public async Task Interrupted_run_left_running_does_not_count_either()
    {
        var monday = await StartAsync();
        await SucceedAsync(monday.Run.Id);

        _time.Now = T0.AddDays(1);
        await StartAsync();                     // never finished (Ctrl+C, container killed)

        _time.Now = T0.AddDays(2);
        var wednesday = await StartAsync();

        Assert.Equal(JobAge.ThreeDays, wednesday.Window.MaxAge);
    }

    [Fact]
    public async Task Success_stores_counts()
    {
        var started = await StartAsync();
        _time.Now = T0.AddMinutes(3);

        await SucceedAsync(started.Run.Id, found: 9, newForUser: 4);

        var run = await LoadAsync(started.Run.Id);
        Assert.Equal((ScoutRunStatus.Succeeded, 9, 4, T0.AddMinutes(3)), (run.Status, run.Found, run.NewForUser, run.FinishedAt));
    }

    [Fact]
    public async Task Failure_stores_exception_type_and_message()
    {
        var started = await StartAsync();

        await FailAsync(started.Run.Id, new HttpRequestException("503 Service Unavailable"));

        var run = await LoadAsync(started.Run.Id);
        Assert.Equal(ScoutRunStatus.Failed, run.Status);
        Assert.Contains("HttpRequestException", run.Error);
        Assert.Contains("503 Service Unavailable", run.Error);
    }

    [Fact]
    public async Task Runs_of_another_user_do_not_affect_the_window()
    {
        await using (var db = _database.CreateContext())
        {
            var other = new User("dev", "autolakyrnik", "", "autolakyrnik", T0);
            var otherRun = new ScoutRun(other.Id, T0.AddHours(-1));
            otherRun.Succeed(1, 1, T0.AddHours(-1));
            db.AddRange(other, otherRun);
            await db.SaveChangesAsync();
        }

        var mine = await StartAsync();

        Assert.Equal(JobAge.ThreeDays, mine.Window.MaxAge);   // my first run
    }
}
