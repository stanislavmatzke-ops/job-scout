using JobScout.Application.Abstractions;
using JobScout.Application.Evaluation;
using JobScout.Application.Limits;
using JobScout.Application.Llm;
using JobScout.Application.Profiles;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Matches;

public enum EvaluateNowStatus { Evaluated, NotFound, NoProfile, AdRemoved, TryLater }

public sealed record EvaluateNowResult(EvaluateNowStatus Status, JobEvaluation? Evaluation = null);

/// <summary>
/// "Ohodnotit": any job of mine – waiting, triaged out, or again. AI pre-selects, the user decides.
/// Limited per user and day (ADR-0021) – <see cref="AiLimitExceededException"/> before anything is called.
/// </summary>
public sealed class EvaluateMatchNow(IAppDbContext db, MatchEvaluator evaluator, AiQuota? quota = null)
{
    public async Task<EvaluateNowResult> ExecuteAsync(Guid userId, Guid matchId, CancellationToken ct = default)
    {
        var match = await db.JobMatches
            .Include(m => m.JobOffer)
            .SingleOrDefaultAsync(m => m.Id == matchId && m.UserId == userId, ct);
        if (match is null)
            return new EvaluateNowResult(EvaluateNowStatus.NotFound);

        var current = await db.CurrentProfileAsync(userId, ct);
        if (current is null)
            return new EvaluateNowResult(EvaluateNowStatus.NoProfile);

        using var slot = quota is null ? null : await quota.EnterAsync(userId, AiAction.Evaluation, ct);
        using var usage = LlmCallContext.Begin(userId);     // on-demand cost belongs to the user, not to a morning run
        var outcome = await evaluator.EvaluateAsync(match, current.Profile, current.Version, ct);
        await db.SaveChangesAsync(ct);

        return outcome.Status switch
        {
            MatchEvaluationStatus.Evaluated => new EvaluateNowResult(EvaluateNowStatus.Evaluated, outcome.Evaluation),
            MatchEvaluationStatus.AdRemoved => new EvaluateNowResult(EvaluateNowStatus.AdRemoved),
            _ => new EvaluateNowResult(EvaluateNowStatus.TryLater)
        };
    }
}
