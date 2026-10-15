using JobScout.Application.Abstractions;
using JobScout.Application.Llm;
using JobScout.Application.Profiles;
using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Evaluation;

public sealed record EvaluatedJob(JobOffer Offer, JobMatch Match, JobEvaluation Evaluation);

public sealed record EvaluateJobsResult(
    IReadOnlyList<EvaluatedJob> Evaluated, int DetailsFetched, int Unavailable, int Remaining, IReadOnlyList<LlmResponse> Calls);

/// <summary>
/// Shortlisted jobs → detail (fetched once, stored on the shared JobOffer) → full evaluation (Smart model) → Evaluated.
/// At most maxPerRun evaluations per run (cost cap); the rest waits in Shortlisted for the next run.
/// </summary>
public sealed class EvaluateJobs(IAppDbContext db, MatchEvaluator matchEvaluator)
{
    /// <summary>≈ $0.013 per evaluation (phase 1) → a run costs at most ≈ $0.20 per user.</summary>
    public const int MaxPerRun = 15;

    /// <summary>
    /// Only jobs with at least this triage relevance are evaluated automatically. Relevance 2 ("probably not, but why not")
    /// waits in Shortlisted and is evaluated on demand from the UI – AI preselects, the user decides (8 Oct).
    /// </summary>
    public const int MinAutoRelevance = 3;

    public async Task<EvaluateJobsResult> ExecuteAsync(
        Guid userId, CandidateProfile profile, int profileVersion, int maxPerRun = MaxPerRun, CancellationToken ct = default)
    {
        var shortlisted = await db.JobMatches
            .Include(m => m.JobOffer)
            // null = triaged before relevance existed → evaluate as before
            // Jobs the user added by hand wait for their click – they decide whether to spend the AI on them (ADR-0013)
            .Where(m => m.UserId == userId && m.Stage == MatchStage.Shortlisted && !m.AddedManually
                        && (m.TriageScore == null || m.TriageScore >= MinAutoRelevance))
            .OrderByDescending(m => m.TriageScore)      // the per-run limit hits the best candidates first
            .ThenBy(m => m.CreatedAt)
            .ToListAsync(ct);

        var evaluated = new List<EvaluatedJob>();
        var calls = new List<LlmResponse>();
        int detailsFetched = 0, unavailable = 0;

        foreach (var match in shortlisted)
        {
            if (evaluated.Count >= maxPerRun)
                break;

            var outcome = await matchEvaluator.EvaluateAsync(match, profile, profileVersion, ct);
            if (outcome.DetailFetched)
                detailsFetched++;

            switch (outcome.Status)
            {
                case MatchEvaluationStatus.AdRemoved:
                    unavailable++;
                    await db.SaveChangesAsync(ct);
                    break;
                case MatchEvaluationStatus.Evaluated:
                    calls.Add(outcome.Response!);
                    evaluated.Add(new EvaluatedJob(match.JobOffer!, match, outcome.Evaluation!));
                    await db.SaveChangesAsync(ct);          // after each job – paid work is never lost
                    break;
                // SourceUnavailable: temporary – the next run tries again
            }
        }

        var remaining = shortlisted.Count - evaluated.Count - unavailable;
        return new EvaluateJobsResult(evaluated, detailsFetched, unavailable, remaining, calls);
    }

}