using System.Net;
using JobScout.Application.Abstractions;
using JobScout.Application.Jobs;
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
public sealed class EvaluateJobs(IAppDbContext db, IEnumerable<IJobSource> sources, JobEvaluator evaluator, TimeProvider time)
{
    /// <summary>≈ $0.013 per evaluation (phase 1) → a run costs at most ≈ $0.20 per user.</summary>
    public const int MaxPerRun = 15;

    /// <summary>
    /// Only jobs with at least this triage relevance are evaluated automatically. Relevance 2 ("probably not, but why not")
    /// waits in Shortlisted and is evaluated on demand from the UI – AI preselects, the user decides (8 Oct).
    /// </summary>
    public const int MinAutoRelevance = 3;

    public async Task<EvaluateJobsResult> ExecuteAsync(
        Guid userId, CandidateProfile profile, int maxPerRun = MaxPerRun, CancellationToken ct = default)
    {
        var shortlisted = await db.JobMatches
            .Include(m => m.JobOffer)
            // null = triaged before relevance existed → evaluate as before
            .Where(m => m.UserId == userId && m.Stage == MatchStage.Shortlisted
                        && (m.TriageScore == null || m.TriageScore >= MinAutoRelevance))
            .OrderByDescending(m => m.TriageScore)      // the per-run limit hits the best candidates first
            .ThenBy(m => m.CreatedAt)
            .ToListAsync(ct);

        var sourceByName = sources.ToDictionary(s => s.Name);
        var evaluated = new List<EvaluatedJob>();
        var calls = new List<LlmResponse>();
        int detailsFetched = 0, unavailable = 0;

        foreach (var match in shortlisted)
        {
            if (evaluated.Count >= maxPerRun)
                break;

            var offer = match.JobOffer!;
            if (offer.DetailText is null)
            {
                if (!sourceByName.TryGetValue(offer.Source, out var source))
                    continue;                                   // source switched off – wait, don't crash

                try
                {
                    offer.SetDetail(await source.GetDetailAsync(offer.ExternalId, ct), time.GetUtcNow());
                    detailsFetched++;
                }
                catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                {
                    match.MarkTriagedOut("Inzerát už není dostupný (stažen z portálu).", time.GetUtcNow());
                    unavailable++;
                    await db.SaveChangesAsync(ct);
                    continue;
                }
                catch (HttpRequestException)
                {
                    continue;                                   // temporary failure – next run tries again
                }
            }

            var result = await evaluator.EvaluateAsync(profile, ComposeJobText(offer), ct);
            calls.Add(result.Response);

            match.ApplyEvaluation(result.Evaluation.Score, LlmJson.Serialize(result.Evaluation), time.GetUtcNow());
            evaluated.Add(new EvaluatedJob(offer, match, result.Evaluation));

            await db.SaveChangesAsync(ct);                      // after each job – paid work is never lost
        }

        var remaining = shortlisted.Count - evaluated.Count - unavailable;
        return new EvaluateJobsResult(evaluated, detailsFetched, unavailable, remaining, calls);
    }

    /// <summary>What the model sees: the listing facts + the detail text, with a warning when it's only a snippet.</summary>
    private static string ComposeJobText(JobOffer offer) => $"""
        Pozice: {offer.Title}
        Firma: {offer.Company}
        Místo: {offer.Location}
        Štítky: {string.Join(", ", offer.Tags)}
        Odkaz: {offer.Url}
        {(offer.DetailIsComplete ? "" : "Pozor: text inzerátu je neúplný (jen útržek) – chybějící údaje ber jako neznámé, ne jako nevýhodu.")}

        {offer.DetailText}
        """;
}