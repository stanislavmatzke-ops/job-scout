using JobScout.Application.Evaluation;
using JobScout.Application.Jobs;
using JobScout.Application.Letters;
using JobScout.Application.Llm;
using JobScout.Application.Profiles;
using JobScout.Application.Triage;
using JobScout.Core.Domain;
using JobScout.Core.Jobs;
using Microsoft.Extensions.Logging;

namespace JobScout.Application.Scouting;

/// <summary>One search of the plan: what was asked, how many ads came back, how many were new in this run.</summary>
/// <param name="Error">The search couldn't be done (the portal was down or sent a page we can't read) – it was skipped.</param>
public sealed record SourceSearch(string Source, PlannedSearch Search, int Found, int Added, string? Error = null);

public sealed record ScoutRunResult(
    ScoutRun Run,
    SearchWindow Window,
    bool PlanIsNew,
    IReadOnlyList<SourceSearch> Searches,
    RecordFoundJobsResult Recorded,
    TriageResult Triage,
    EvaluateJobsResult Evaluation,
    WriteCoverLettersResult Letters,
    UsageSummary Cost);

/// <summary>
/// The daily scout for one user: search plan → run journal → search every source → record → triage →
/// evaluate → cover letters → cost. One pipeline for the Worker today and the API / Azure job later.
/// The run ends as Succeeded or Failed; every AI call inside is billed to it (ADR-0016).
/// </summary>
public sealed class RunScout(
    PlanSearch planSearch,
    ScoutRunJournal journal,
    IEnumerable<IJobSource> sources,
    RecordFoundJobs recordFoundJobs,
    TriageJobs triageJobs,
    EvaluateJobs evaluateJobs,
    WriteCoverLetters writeCoverLetters,
    UsageReport usageReport,
    ILogger<RunScout> logger)
{
    /// <summary>Pages per search – plenty for a 24 h / 3-day window.</summary>
    public const int MaxPagesPerSearch = 5;

    public async Task<ScoutRunResult> ExecuteAsync(AnalyzedProfile analyzed, CancellationToken ct = default)
    {
        var userId = analyzed.User.Id;

        SearchPlanResult planned;
        using (LlmCallContext.Begin(userId))      // the plan belongs to the profile version, not to this run
            planned = await planSearch.ExecuteAsync(analyzed.Version, analyzed.Profile, ct);
        logger.LogInformation(planned.IsNew ? "Nový plán hledání" : "Plán hledání z DB – AI ani ověřování se nevolaly");

        var started = await journal.StartAsync(userId, ct);
        using var billing = LlmCallContext.Begin(userId, started.Run.Id);   // everything below is billed to this run
        logger.LogInformation("Okno hledání: {Window}",
            started.Window.MaxAge == JobAge.Day ? "posledních 24 h" : "poslední 3 dny");
        if (started.Window.HasGap)
            logger.LogWarning("Poslední úspěšný běh je starší než 3 dny – část inzerátů mohla uniknout");

        try
        {
            var (listings, searches) = await SearchAsync(planned.Plan, started.Window.MaxAge, ct);

            var recorded = await recordFoundJobs.ExecuteAsync(userId, analyzed.Version.Version, listings, ct);
            logger.LogInformation("Nalezeno {Found} nabídek, nových pro uživatele {New}, už viděných {Seen}",
                recorded.Found, recorded.NewForUser.Count, recorded.AlreadySeen);

            var triage = await triageJobs.ExecuteAsync(userId, analyzed.Profile, ct);
            logger.LogInformation("Třídění: ponecháno {Kept}, vyřazeno {Dropped}", triage.Kept, triage.Dropped);

            var evaluation = await evaluateJobs.ExecuteAsync(userId, analyzed.Profile, analyzed.Version.Version, ct: ct);
            logger.LogInformation("Hodnocení: ohodnoceno {Count}, nedostupných {Unavailable}, čeká na další běh {Remaining}",
                evaluation.Evaluated.Count, evaluation.Unavailable, evaluation.Remaining);

            var letters = await writeCoverLetters.ExecuteAsync(userId, analyzed.Profile, ct: ct);
            logger.LogInformation("Dopisy: napsáno {Count}, nepovedlo se {Failed}, čeká na další běh {Remaining}",
                letters.Letters.Count, letters.Failed, letters.Remaining);

            var cost = await usageReport.ForRunAsync(started.Run.Id, ct);

            // Some searches skipped: the user still gets what was found, but the run counts as failed – the next run
            // (the retry an hour later, or the next night) then searches back over the window and catches up
            var skipped = searches.Where(s => s.Error is not null).ToList();
            if (skipped.Count > 0)
                await journal.FailAsync(started.Run, new IncompleteSearchException(
                    $"Hledání neproběhlo celé – přeskočeno {skipped.Count} z {searches.Count}: {skipped[0].Error}"), ct);
            else
                await journal.SucceedAsync(started.Run, recorded.Found, recorded.NewForUser.Count, ct);

            return new ScoutRunResult(started.Run, started.Window, planned.IsNew, searches,
                recorded, triage, evaluation, letters, cost);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Record the failure (even if the token is already cancelled), then let the caller see it
            await journal.FailAsync(started.Run, ex, CancellationToken.None);
            throw;
        }
    }

    /// <summary>Every planned search of every source; an ad found by several searches is kept once.</summary>
    private async Task<(IReadOnlyList<JobListing> Listings, IReadOnlyList<SourceSearch> Searches)> SearchAsync(
        SearchPlan plan, JobAge window, CancellationToken ct)
    {
        var unique = new Dictionary<(string Source, string ExternalId), JobListing>();
        var searches = new List<SourceSearch>();

        HttpRequestException? firstError = null;
        foreach (var source in sources)
        foreach (var search in plan.For(source.Name))
        {
            IReadOnlyList<JobListing> found;
            try
            {
                found = await source.SearchAsync(search.ToQuery(window), MaxPagesPerSearch, ct);
            }
            catch (HttpRequestException ex)
            {
                // One search the portal won't answer (down, or its beta site) must not cost the user the others
                firstError ??= ex;
                searches.Add(new SourceSearch(source.Name, search, 0, 0, ex.Message));
                logger.LogWarning("  {What}: přeskočeno – {Error}", search.Category ?? $"text \"{search.Keywords}\"", ex.Message);
                continue;
            }
            var added = found.Count(l => unique.TryAdd((l.Source, l.ExternalId), l));
            searches.Add(new SourceSearch(source.Name, search, found.Count, added));

            logger.LogInformation("  {What} @ {Where}: {Found} nalezeno, {Added} unikátních v tomto běhu",
                search.Category ?? $"text \"{search.Keywords}\"",
                search.Location is null ? "celá ČR" : $"{search.Location} + {search.RadiusKm} km",
                found.Count, added);
        }

        // Not a single search went through – the source is out, the whole run fails (and the admins hear about it)
        if (firstError is not null && searches.All(s => s.Error is not null))
            throw firstError;

        return (unique.Values.ToList(), searches);
    }
}

/// <summary>Recorded with a run that skipped some searches – the run counts as failed, so the next one catches up.</summary>
public sealed class IncompleteSearchException(string message) : Exception(message);
