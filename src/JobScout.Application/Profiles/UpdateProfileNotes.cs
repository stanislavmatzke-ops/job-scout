using JobScout.Application.Abstractions;
using JobScout.Application.Limits;
using JobScout.Application.Llm;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Profiles;

/// <summary>New notes ("full remote, no Java…") → re-analysis of the stored CV. Limited per user and day (ADR-0021).</summary>
public sealed class UpdateProfileNotes(IAppDbContext db, AnalyzeProfile analyze, AiQuota? quota = null)
{
    public const int MaxLength = 4000;

    /// <returns>null = no CV stored yet – upload one first</returns>
    public async Task<AnalyzedProfile?> ExecuteAsync(Guid userId, string? notes, CancellationToken ct = default)
    {
        notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (notes?.Length > MaxLength)
            throw new ArgumentException($"Poznámky mohou mít nejvýš {MaxLength} znaků.", nameof(notes));

        var cv = await db.StoredCvs.AsNoTracking().SingleOrDefaultAsync(c => c.UserId == userId, ct);
        if (cv is null)
            return null;

        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);
        using var slot = quota is not null && await analyze.WouldCallAiAsync(userId, cv.Content, notes, ct)
            ? await quota.EnterAsync(userId, AiAction.Profile, ct)
            : null;
        return await analyze.ExecuteAsync(new ProfileInput(
            user.Provider, user.Subject, user.DisplayName, cv.FileName, new LlmDocument("CV", cv.MediaType, cv.Content), notes), ct);
    }
}
