using JobScout.Application.Abstractions;
using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Scouting;

/// <summary>
/// A new user should not wait for the morning: once they have a profile and have never been searched for,
/// the search runs right away in the background (6h). Afterwards only the morning run searches.
/// A failed first search is tried again with the next CV upload (there is still no successful run).
/// </summary>
public sealed class StartFirstScout(IAppDbContext db, IScoutQueue queue)
{
    /// <returns>true = the first search is queued now</returns>
    public async Task<bool> ExecuteAsync(Guid userId, CancellationToken ct = default)
    {
        var approved = await db.Users.AnyAsync(u => u.Id == userId && u.Access == AccessStatus.Approved, ct);
        if (!approved)
            return false;                                    // the AI costs money – only approved accounts (ADR-0017)

        var hasProfile = await db.UserProfiles.AnyAsync(p => p.UserId == userId, ct);
        var searchedBefore = await db.ScoutRuns.AnyAsync(
            r => r.UserId == userId && r.Status != ScoutRunStatus.Failed, ct);   // succeeded, or running in the Worker
        if (!hasProfile || searchedBefore)
            return false;

        return queue.TryEnqueue(userId);
    }
}
