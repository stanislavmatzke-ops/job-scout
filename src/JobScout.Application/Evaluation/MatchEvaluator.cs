using System.Net;
using JobScout.Application.Jobs;
using JobScout.Application.Llm;
using JobScout.Application.Profiles;
using JobScout.Core.Domain;

namespace JobScout.Application.Evaluation;

public enum MatchEvaluationStatus { Evaluated, AdRemoved, SourceUnavailable }

public sealed record MatchEvaluation(
    MatchEvaluationStatus Status, JobEvaluation? Evaluation = null, LlmResponse? Response = null, bool DetailFetched = false);

/// <summary>
/// One job: make sure the full ad is there (fetched once, stored on the shared offer), then evaluate it.
/// Used by the morning run (EvaluateJobs) and by the "Ohodnotit" button (EvaluateMatchNow). Doesn't save – the caller does.
/// </summary>
public sealed class MatchEvaluator(IEnumerable<IJobSource> sources, JobEvaluator evaluator, TimeProvider time)
{
    public async Task<MatchEvaluation> EvaluateAsync(JobMatch match, CandidateProfile profile, int profileVersion, CancellationToken ct = default)
    {
        var offer = match.JobOffer ?? throw new InvalidOperationException("JobMatch.JobOffer must be loaded.");
        var fetched = false;

        if (offer.DetailText is null)
        {
            var source = sources.FirstOrDefault(s => s.Name == offer.Source);
            if (source is null)
                return new MatchEvaluation(MatchEvaluationStatus.SourceUnavailable);   // source switched off – wait, don't crash

            try
            {
                offer.SetDetail(await source.GetDetailAsync(offer.ExternalId, ct), time.GetUtcNow());
                fetched = true;
            }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                match.MarkTriagedOut("Inzerát už není dostupný (stažen z portálu).", time.GetUtcNow());
                return new MatchEvaluation(MatchEvaluationStatus.AdRemoved);
            }
            catch (HttpRequestException)
            {
                return new MatchEvaluation(MatchEvaluationStatus.SourceUnavailable);   // temporary – try again later
            }
        }

        var result = await evaluator.EvaluateAsync(profile, ComposeJobText(offer), ct);
        match.ApplyEvaluation(result.Evaluation.Score, LlmJson.Serialize(result.Evaluation), profileVersion, time.GetUtcNow());

        return new MatchEvaluation(MatchEvaluationStatus.Evaluated, result.Evaluation, result.Response, fetched);
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
