using JobScout.Application.Abstractions;
using JobScout.Application.Users;
using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Scouting;

/// <summary>Who gets the morning run: approved users with an analysed profile (ADR-0017 – pending and suspended cost nothing).</summary>
public sealed class ListScoutUsers(IAppDbContext db)
{
    /// <param name="withoutSuccessSince">The retry an hour later (NightSchedule): only users without a successful run since then.</param>
    public async Task<IReadOnlyList<Guid>> ExecuteAsync(bool includeDevPersonas = false, CancellationToken ct = default,
        DateTimeOffset? withoutSuccessSince = null) =>
        await db.Users
            .Where(u => u.Access == AccessStatus.Approved)
            .Where(u => includeDevPersonas || u.Provider != SignInUser.DevProvider)
            .Where(u => db.UserProfiles.Any(p => p.UserId == u.Id))
            .Where(u => withoutSuccessSince == null || !db.ScoutRuns.Any(r =>
                r.UserId == u.Id && r.Status == ScoutRunStatus.Succeeded && r.StartedAt >= withoutSuccessSince))
            .OrderBy(u => u.CreatedAt)
            .Select(u => u.Id)
            .ToListAsync(ct);
}
