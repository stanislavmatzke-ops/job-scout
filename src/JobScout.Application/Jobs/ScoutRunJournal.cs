using JobScout.Application.Abstractions;
using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Jobs;

public sealed record StartedRun(ScoutRun Run, SearchWindow Window);

/// <summary>Start / finish of a user's search run, persisted immediately (a crash still leaves a trace).</summary>
public sealed class ScoutRunJournal(IAppDbContext db, TimeProvider time)
{
    public async Task<StartedRun> StartAsync(Guid userId, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var lastSuccess = await db.ScoutRuns
            .Where(r => r.UserId == userId && r.Status == ScoutRunStatus.Succeeded)
            .OrderByDescending(r => r.StartedAt)
            .Select(r => (DateTimeOffset?)r.StartedAt)
            .FirstOrDefaultAsync(ct);

        var run = new ScoutRun(userId, now);
        db.ScoutRuns.Add(run);
        await db.SaveChangesAsync(ct);

        return new StartedRun(run, SearchWindow.For(lastSuccess, now));
    }

    public async Task SucceedAsync(ScoutRun run, int found, int newForUser, CancellationToken ct = default)
    {
        run.Succeed(found, newForUser, time.GetUtcNow());
        await db.SaveChangesAsync(ct);
    }

    public async Task FailAsync(ScoutRun run, Exception error, CancellationToken ct = default)
    {
        run.Fail($"{error.GetType().Name}: {error.Message}", time.GetUtcNow());
        await db.SaveChangesAsync(ct);
    }
}