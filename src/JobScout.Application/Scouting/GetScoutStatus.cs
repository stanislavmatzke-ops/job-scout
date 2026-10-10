using JobScout.Application.Abstractions;
using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Scouting;

/// <summary>Searching = the background search for this user is waiting or running; LastSearchAt = the last successful one.</summary>
public sealed record ScoutStatus(bool Searching, DateTimeOffset? LastSearchAt);

public sealed class GetScoutStatus(IAppDbContext db, IScoutQueue queue)
{
    public async Task<ScoutStatus> ExecuteAsync(Guid userId, CancellationToken ct = default)
    {
        var finished = await db.ScoutRuns.AsNoTracking()
            .Where(r => r.UserId == userId && r.Status == ScoutRunStatus.Succeeded)
            .Select(r => r.FinishedAt)
            .ToListAsync(ct);                                // a few per user; max in memory (DateTimeOffset is binary in SQLite)

        return new ScoutStatus(queue.IsBusy(userId), finished.Max());
    }
}
