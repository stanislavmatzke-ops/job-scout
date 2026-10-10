using JobScout.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Matches;

/// <summary>The user's own edit of the letter. Never empty – an empty letter would be rewritten by the next morning run.</summary>
public sealed class UpdateCoverLetter(IAppDbContext db, TimeProvider time)
{
    public const int MaxLength = 10_000;

    public async Task<bool> ExecuteAsync(Guid userId, Guid matchId, string? text, CancellationToken ct = default)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text))
            throw new ArgumentException("Dopis nesmí být prázdný.", nameof(text));
        if (text.Length > MaxLength)
            throw new ArgumentException($"Dopis může mít nejvýš {MaxLength} znaků.", nameof(text));

        var match = await db.JobMatches.SingleOrDefaultAsync(m => m.Id == matchId && m.UserId == userId, ct);
        if (match is null)
            return false;

        match.SetCoverLetter(text, time.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return true;
    }
}
