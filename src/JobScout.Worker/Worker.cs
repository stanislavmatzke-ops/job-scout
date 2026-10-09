using JobScout.Application.Evaluation;
using JobScout.Application.Jobs;
using JobScout.Application.Letters;
using JobScout.Application.Llm;
using JobScout.Application.Profiles;
using JobScout.Application.Triage;
using JobScout.Core.Jobs;

namespace JobScout.Worker;

/// <summary>
/// One-shot run for one user (a persona or the data folder):
/// CV + notes → profile (only when changed), then either
///  - Scout:Mode=search → search plan → search → store, list only new jobs, or
///  - default → evaluate the sample job ads in the data folder (jobs/*.md).
/// </summary>
public class Worker(
    IServiceScopeFactory scopeFactory,
    JobEvaluator evaluator,
    IJobSource jobSource,
    IConfiguration config,
    IHostEnvironment env,
    IHostApplicationLifetime lifetime,
    ILogger<Worker> logger) : BackgroundService
{
    private long _inputTokens;
    private long _outputTokens;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Until SSO (phase 5): identity "dev" + persona name, or the data folder name ("samples" / "private")
            var persona = config["Scout:Persona"];
            var dataDir = ResolveDataDir(persona);
            var subject = persona ?? Path.GetFileName(dataDir);

            var (cv, cvFileName) = await LoadCvAsync(dataDir, stoppingToken);
            var notes = await ReadOptionalAsync(Path.Combine(dataDir, "preferences.md"), stoppingToken);

            await using var scope = scopeFactory.CreateAsyncScope();
            var analyzeProfile = scope.ServiceProvider.GetRequiredService<AnalyzeProfile>();

            var analyzed = await analyzeProfile.ExecuteAsync(
                new ProfileInput("dev", subject, subject, cvFileName, cv, notes), stoppingToken);

            if (analyzed.IsNew)
            {
                logger.LogInformation("Nová verze profilu v{Version} pro dev:{Subject}", analyzed.Version.Version, subject);
                LogTokens("profil", analyzed.Response!);
            }
            else
                logger.LogInformation("Profil beze změny (v{Version}, dev:{Subject}) – AI se nevolala, 0 tokenů",
                    analyzed.Version.Version, subject);

            LogProfile(analyzed.Profile);

            if (config["Scout:Mode"] == "search")
                await SearchAsync(scope.ServiceProvider, analyzed, subject, stoppingToken);
            else
                using (LlmCallContext.Begin(analyzed.User.Id))
                    await EvaluateSamplesAsync(dataDir, analyzed.Profile, stoppingToken);

            logger.LogInformation("Celkem tokenů: in={In}, out={Out}", _inputTokens, _outputTokens);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host is shutting down (Ctrl+C, Rider Stop, container restart) – not a failure
            logger.LogWarning("Běh přerušen – aplikace se vypíná");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Běh selhal");
            Environment.ExitCode = 1;
        }
        finally
        {
            lifetime.StopApplication();
        }
    }

    /// <summary>Search plan → run journal → search → store → list only the jobs new for this user.</summary>
    private async Task SearchAsync(IServiceProvider services, AnalyzedProfile analyzed, string subject, CancellationToken ct)
    {
        SearchPlanResult planned;
        using (LlmCallContext.Begin(analyzed.User.Id))
            planned = await services.GetRequiredService<PlanSearch>().ExecuteAsync(analyzed.Version, analyzed.Profile, ct);

        logger.LogInformation(planned.IsNew ? "Nový plán hledání" : "Plán hledání z DB – AI ani ověřování se nevolaly");
        foreach (var call in planned.Calls)
            LogTokens("plán hledání", call);

        var journal = services.GetRequiredService<ScoutRunJournal>();
        var recordFoundJobs = services.GetRequiredService<RecordFoundJobs>();

        var started = await journal.StartAsync(analyzed.User.Id, ct);
        using var usage = LlmCallContext.Begin(analyzed.User.Id, started.Run.Id);   // everything below is billed to this run
        var window = started.Window.MaxAge;
        logger.LogInformation("Okno hledání: {Window}", window == JobSearch.JobAge.Day ? "posledních 24 h" : "poslední 3 dny");
        if (started.Window.HasGap)
            logger.LogWarning("Poslední úspěšný běh je starší než 3 dny – část inzerátů mohla uniknout");

        try
        {
            var unique = new Dictionary<string, JobListing>();
            foreach (var s in planned.Plan.For(jobSource.Name))
            {
                var found = await jobSource.SearchAsync(s.ToQuery(window), maxPages: 5, ct);
                var added = found.Count(l => unique.TryAdd(l.ExternalId, l));
                logger.LogInformation("  {What} @ {Where}: {Found} nalezeno, {Added} nových v tomto běhu",
                    s.Category ?? $"text \"{s.Keywords}\"",
                    s.Location is null ? "celá ČR" : $"{s.Location} + {s.RadiusKm} km",
                    found.Count, added);
            }

            var recorded = await recordFoundJobs.ExecuteAsync(
                analyzed.User.Id, analyzed.Version.Version, unique.Values.ToList(), ct);

            logger.LogInformation(
                "Nalezeno {Found} nabídek: NOVÝCH pro dev:{Subject} {New}, už viděných {Seen} (nových v DB celkem {NewOffers})",
                recorded.Found, subject, recorded.NewForUser.Count, recorded.AlreadySeen, recorded.NewOffers);

            foreach (var job in recorded.NewForUser)
                logger.LogInformation("  NOVÁ {Id} | {Title} | {Company} | {Location}",
                    job.Offer.ExternalId, job.Offer.Title, job.Offer.Company, job.Offer.Location);
            var triage = await services.GetRequiredService<TriageJobs>()
                .ExecuteAsync(analyzed.User.Id, analyzed.Profile, ct);
            foreach (var call in triage.Calls)
                LogTokens("třídění", call);

            logger.LogInformation("Třídění: {Total} čekajících → ponecháno {Kept}, vyřazeno {Dropped}",
                triage.Jobs.Count, triage.Kept, triage.Dropped);
            foreach (var job in triage.Jobs.OrderByDescending(j => j.Relevance))
                logger.LogInformation("  {Mark} {Relevance} {Title} | {Company}{Reason}",
                    job.Keep ? "✔" : "✘", job.Relevance, job.Offer.Title, job.Offer.Company, job.Keep ? "" : $" – {job.Reason}");
            
            var evaluation = await services.GetRequiredService<EvaluateJobs>()
                .ExecuteAsync(analyzed.User.Id, analyzed.Profile, ct: ct);
            foreach (var call in evaluation.Calls)
                LogTokens("hodnocení", call);

            logger.LogInformation(
                "Hodnocení: ohodnoceno {Evaluated}, staženo detailů {Details}, nedostupných {Unavailable}, čeká na další běh {Remaining}",
                evaluation.Evaluated.Count, evaluation.DetailsFetched, evaluation.Unavailable, evaluation.Remaining);

            foreach (var job in evaluation.Evaluated.OrderByDescending(j => j.Evaluation.Score))
                logger.LogInformation("  {Score}/10  {Title} | {Company} – {Summary}",
                    job.Evaluation.Score, job.Offer.Title, job.Offer.Company, job.Evaluation.Summary);
            
            var letters = await services.GetRequiredService<WriteCoverLetters>()
                .ExecuteAsync(analyzed.User.Id, analyzed.Profile, ct: ct);
            foreach (var call in letters.Calls)
                LogTokens("dopis", call);

            logger.LogInformation("Dopisy: napsáno {Count}, nepovedlo se {Failed}, čeká na další běh {Remaining}",
                letters.Letters.Count, letters.Failed, letters.Remaining);
            if (letters.Failed > 0)
                logger.LogWarning("Některé dopisy byly useknuté nebo prázdné – zkusí se znovu při dalším běhu");
            
            foreach (var l in letters.Letters)
                logger.LogInformation("""
                                      ── DOPIS: {Title} | {Company} ({Score}/10) ──
                                      {Letter}
                                      """, l.Offer.Title, l.Offer.Company, l.Match.Score, l.Letter);
            
            var cost = await services.GetRequiredService<UsageReport>().ForRunAsync(started.Run.Id, ct);
            foreach (var line in cost.Lines)
                logger.LogInformation("  {Purpose}: {Calls}× (chyb {Failed}), in={In}, out={Out}, ${Cost:0.0000}",
                    line.Purpose, line.Calls, line.Failed, line.InputTokens, line.OutputTokens, line.CostMicroUsd / 1_000_000m);
            logger.LogInformation("Náklady běhu: ${Total:0.000}", cost.TotalUsd);
            
            await journal.SucceedAsync(started.Run, recorded.Found, recorded.NewForUser.Count, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Record the failure even while everything is being cancelled, then let the run fail
            await journal.FailAsync(started.Run, ex, CancellationToken.None);
            throw;
        }
    }

    /// <summary>Default mode: evaluate the sample job ads from the data folder (jobs/*.md) against the profile.</summary>
    private async Task EvaluateSamplesAsync(string dataDir, CandidateProfile profile, CancellationToken ct)
    {
        var jobsDir = Path.Combine(dataDir, "jobs");
        var jobFiles = Directory.Exists(jobsDir)
            ? Directory.GetFiles(jobsDir, "*.md").Order().ToList()
            : [];
        logger.LogInformation("Nabídek k hodnocení: {Count}", jobFiles.Count);

        foreach (var jobFile in jobFiles)
        {
            var jobText = await File.ReadAllTextAsync(jobFile, ct);
            var result = await evaluator.EvaluateAsync(profile, jobText, ct);

            LogEvaluation(Path.GetFileName(jobFile), result.Evaluation);
            LogTokens(Path.GetFileName(jobFile), result.Response);
        }
    }

    // Relative path = inside the project (samples in the repo); absolute path = own data outside the repo
    private string ResolveDataDir(string? persona)
    {
        var configured = persona is null
            ? config["Scout:DataDir"] ?? "samples"
            : Path.Combine("samples", "personas", persona);
        var path = Path.GetFullPath(Path.Combine(env.ContentRootPath, configured));
        var source = Path.IsPathRooted(configured) ? "PRIVATE (vlastní data mimo repo)" : "SAMPLE (ukázková data z repa)";

        logger.LogInformation("Zdroj dat: {Source} – {Path}", source, path);

        return Directory.Exists(path)
            ? path
            : throw new DirectoryNotFoundException($"Složka s daty neexistuje: {path}");
    }

    private async Task<(LlmDocument Cv, string FileName)> LoadCvAsync(string dataDir, CancellationToken ct)
    {
        var pdf = Path.Combine(dataDir, "cv.pdf");
        var md = Path.Combine(dataDir, "cv.md");

        var (path, mediaType) = File.Exists(pdf) ? (pdf, "application/pdf")
            : File.Exists(md) ? (md, "text/plain")
            : throw new FileNotFoundException($"Ve složce {dataDir} chybí cv.pdf nebo cv.md");

        var content = await File.ReadAllBytesAsync(path, ct);
        logger.LogInformation("CV: {File} ({MediaType}, {Kb} kB)", Path.GetFileName(path), mediaType, content.Length / 1024);

        return (new LlmDocument("CV", mediaType, content), Path.GetFileName(path));
    }

    private async Task<string?> ReadOptionalAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            logger.LogInformation("Poznámky kandidáta: žádné ({File} neexistuje)", Path.GetFileName(path));
            return null;
        }

        logger.LogInformation("Poznámky kandidáta: {File}", Path.GetFileName(path));
        return await File.ReadAllTextAsync(path, ct);
    }

    private void LogProfile(CandidateProfile p)
    {
        var pref = p.Preferences;
        var s = p.Search;
        logger.LogInformation("""
            ── PROFIL ──
            {Headline}
            Praxe: {Years} let, {Seniority}
            Cílové role: {Roles}
            Hlavní dovednosti: {Core}
            Další: {Other}
            Obory: {Domains} | Jazyky: {Languages}
            Preference: HO {Remote} (přijatelné: {Acceptable}), forma {Types}, min. mzda {Salary} Kč
            Priority: {Priorities}
            Vyhnout se: {Avoid}
            Hledat: {Professions} | zúžit: {Keywords}
            Kde: {Home} + {Radius} km, navíc: {Extra}
            """,
            p.Headline, p.YearsOfExperience, p.Seniority,
            string.Join(", ", p.TargetRoles), string.Join(", ", p.CoreSkills), string.Join(", ", p.OtherSkills),
            string.Join(", ", p.Domains), string.Join(", ", p.Languages),
            pref.RemoteWork, string.Join("/", pref.AcceptableRemoteWork), string.Join("/", pref.EmploymentTypes), pref.MinMonthlySalaryCzk,
            string.Join(" > ", pref.Priorities), string.Join(", ", pref.Avoid),
            string.Join(", ", s.Professions), string.Join(", ", s.Keywords),
            string.IsNullOrEmpty(s.HomeLocation) ? "neuvedeno" : s.HomeLocation, s.CommuteRadiusKm,
            s.ExtraLocations.Count == 0 ? "–" : string.Join(", ", s.ExtraLocations));
    }

    private void LogEvaluation(string file, JobEvaluation e)
    {
        logger.LogInformation("""
            ── NABÍDKA {File} ──
            Skóre {Score}/10 – {Summary}
            Proč: {Reasons}
            Rizika: {Risks}
            Ověřit: {Questions}
            Mzda: {Salary} | HO: {Remote} | forma: {Type}
            """,
            file, e.Score, e.Summary,
            string.Join(" | ", e.Reasons), string.Join(" | ", e.Risks), string.Join(" | ", e.Questions),
            string.IsNullOrEmpty(e.Salary) ? "neuvedena" : e.Salary, e.RemoteWork, e.EmploymentType);
    }

    private void LogTokens(string what, LlmResponse r)
    {
        _inputTokens += r.InputTokens;
        _outputTokens += r.OutputTokens;
        logger.LogInformation("Tokeny ({What}): in={In}, out={Out}, model {Model}, stop {Stop}",
            what, r.InputTokens, r.OutputTokens, r.Model, r.StopReason);
    }
}
