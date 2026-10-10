using JobScout.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Matches;

/// <summary>Free-text notes on a job – no history entry (that is for status changes).</summary>
public sealed class UpdateMatchNotes(IAppDbContext db, TimeProvider time)
{
    public const int MaxLength = 4000;

    public async Task<bool> ExecuteAsync(Guid userId, Guid matchId, string? notes, CancellationToken ct = default)
    {
        notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (notes?.Length > MaxLength)
            throw new ArgumentException($"Poznámky mohou mít nejvýš {MaxLength} znaků.", nameof(notes));

        var match = await db.JobMatches.SingleOrDefaultAsync(m => m.Id == matchId && m.UserId == userId, ct);
        if (match is null)
            return false;

        match.UpdateNotes(notes, time.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return true;
    }
}
