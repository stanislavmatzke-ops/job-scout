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
}
