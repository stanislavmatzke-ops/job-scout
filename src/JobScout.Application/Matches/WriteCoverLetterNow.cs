using JobScout.Application.Abstractions;
using JobScout.Application.Letters;
using JobScout.Application.Limits;
using JobScout.Application.Llm;
using JobScout.Application.Profiles;
using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Matches;

public enum WriteLetterStatus { Written, NotFound, NoProfile, NotEvaluated, AlreadyWritten, Failed }

public sealed record WriteLetterResult(WriteLetterStatus Status, string? Letter = null);

/// <summary>
/// "Napsat dopis": for any evaluated job, whatever the score. An existing letter (maybe edited by the user)
/// is replaced only when asked for explicitly. Limited per user and day, rewrites also per job (ADR-0021).
/// </summary>
public sealed class WriteCoverLetterNow(IAppDbContext db, CoverLetterWriter writer, TimeProvider time, AiQuota? quota = null)
{
    public async Task<WriteLetterResult> ExecuteAsync(Guid userId, Guid matchId, bool replace = false, CancellationToken ct = default)
    {
        var match = await db.JobMatches
            .Include(m => m.JobOffer)
            .SingleOrDefaultAsync(m => m.Id == matchId && m.UserId == userId, ct);
        if (match is null)
            return new WriteLetterResult(WriteLetterStatus.NotFound);
        if (match.Stage != MatchStage.Evaluated)
            return new WriteLetterResult(WriteLetterStatus.NotEvaluated);           // the letter builds on the evaluation
        if (match.CoverLetter is not null && !replace)
            return new WriteLetterResult(WriteLetterStatus.AlreadyWritten, match.CoverLetter);

        var current = await db.CurrentProfileAsync(userId, ct);
        if (current is null)
            return new WriteLetterResult(WriteLetterStatus.NoProfile);

        if (quota is not null)
            await quota.EnsureLetterRewriteAllowedAsync(userId, match, ct);
        using var slot = quota is null ? null : await quota.EnterAsync(userId, AiAction.CoverLetter, ct);
        using var usage = LlmCallContext.Begin(userId);
        var gender = await db.Users.Where(u => u.Id == userId).Select(u => u.Gender).SingleAsync(ct);
        var draft = await writer.WriteAsync(current.Profile, match, gender, ct);
        if (draft.Letter is null)
            return new WriteLetterResult(WriteLetterStatus.Failed);

        if (match.CoverLetter is null)
            match.SetCoverLetter(draft.Letter, time.GetUtcNow());
        else
            match.RewriteCoverLetter(draft.Letter, time.GetUtcNow());     // counted – a few per job (ADR-0021)
        await db.SaveChangesAsync(ct);
        return new WriteLetterResult(WriteLetterStatus.Written, draft.Letter);
    }
}
