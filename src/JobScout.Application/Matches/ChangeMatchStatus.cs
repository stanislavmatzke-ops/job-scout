using JobScout.Application.Abstractions;
using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Matches;

/// <summary>Status + optional note; the domain records every change in the history.</summary>
public sealed class ChangeMatchStatus(IAppDbContext db, TimeProvider time)
{
    public const int MaxNoteLength = 2000;   // = JobMatchEvent.Note column

    /// <returns>false = no such job for this user (somebody else's job looks like a missing one)</returns>
    public async Task<bool> ExecuteAsync(Guid userId, Guid matchId, MatchStatus status, string? note, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status), status, "Neznámý stav.");

        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (note?.Length > MaxNoteLength)
            throw new ArgumentException($"Poznámka může mít nejvýš {MaxNoteLength} znaků.", nameof(note));

        var match = await db.JobMatches.SingleOrDefaultAsync(m => m.Id == matchId && m.UserId == userId, ct);
        if (match is null)
            return false;

        match.ChangeStatus(status, note, time.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Bulk action on the list ("Zamítnuto" for ten jobs at once) – most a user selects on one page.</summary>
    public const int MaxBulk = 500;

    /// <summary>
    /// The same status (and note) for several jobs at once; each one gets its own history entry.
    /// Somebody else's or unknown ids are skipped silently – like a missing job.
    /// </summary>
    /// <returns>how many of my jobs were found (and changed, unless they already had the status)</returns>
    public async Task<int> ExecuteManyAsync(Guid userId, IReadOnlyCollection<Guid> matchIds, MatchStatus status, string? note,
        CancellationToken ct = default)
    {
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status), status, "Neznámý stav.");
        if (matchIds.Count == 0)
            throw new ArgumentException("Vyberte alespoň jednu nabídku.", nameof(matchIds));
        if (matchIds.Count > MaxBulk)
            throw new ArgumentException($"Najednou lze změnit nejvýš {MaxBulk} nabídek.", nameof(matchIds));

        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (note?.Length > MaxNoteLength)
            throw new ArgumentException($"Poznámka může mít nejvýš {MaxNoteLength} znaků.", nameof(note));

        var ids = matchIds.Distinct().ToList();
        var matches = await db.JobMatches
            .Where(m => m.UserId == userId && ids.Contains(m.Id))
            .ToListAsync(ct);

        var now = time.GetUtcNow();
        foreach (var match in matches)
            match.ChangeStatus(status, note, now);
        await db.SaveChangesAsync(ct);
        return matches.Count;
    }
}
