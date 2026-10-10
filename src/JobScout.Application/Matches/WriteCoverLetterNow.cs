using JobScout.Application.Abstractions;
using JobScout.Application.Letters;
using JobScout.Application.Llm;
using JobScout.Application.Profiles;
using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Matches;

public enum WriteLetterStatus { Written, NotFound, NoProfile, NotEvaluated, AlreadyWritten, Failed }

public sealed record WriteLetterResult(WriteLetterStatus Status, string? Letter = null);

/// <summary>
/// "Napsat dopis": for any evaluated job, whatever the score. An existing letter (maybe edited by the user)
/// is replaced only when asked for explicitly.
/// </summary>
public sealed class WriteCoverLetterNow(IAppDbContext db, CoverLetterWriter writer, TimeProvider time)
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

        var profile = await db.CurrentProfileAsync(userId, ct);
        if (profile is null)
            return new WriteLetterResult(WriteLetterStatus.NoProfile);

        using var usage = LlmCallContext.Begin(userId);
        var gender = await db.Users.Where(u => u.Id == userId).Select(u => u.Gender).SingleAsync(ct);
        var draft = await writer.WriteAsync(profile, match,gender, ct);
        if (draft.Letter is null)
            return new WriteLetterResult(WriteLetterStatus.Failed);

        match.SetCoverLetter(draft.Letter, time.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return new WriteLetterResult(WriteLetterStatus.Written, draft.Letter);
    }
}
