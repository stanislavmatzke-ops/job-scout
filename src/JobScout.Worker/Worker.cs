using JobScout.Application.Evaluation;
using JobScout.Application.Jobs;
using JobScout.Core.Jobs;
using JobScout.Application.Llm;
using JobScout.Application.Profiles;

namespace JobScout.Worker;

/// <summary>
/// One-shot run: CV + notes → profile, then every job in the data folder → evaluation.
/// Data folder layout: cv.pdf | cv.md, preferences.md (optional), jobs/*.md
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

            if (config["Scout:Mode"] == "search")
            {
                // Temporary hand-made plan – in phase 3 the AI picks categories from jobSource.Categories
                string[] categories = ["sw-architekt", "net-vyvojar", "java-vyvojar", "vedouci-vyvoje-team-leader"];

                var unique = new Dictionary<string, JobListing>();
                foreach (var category in categories)
                {
                    var query = new JobSearch.JobSearchQuery { Category = category, Location = "Praha", MaxAge = JobSearch.JobAge.ThreeDays };
                    var found = await jobSource.SearchAsync(query, maxPages: 5, stoppingToken);
                    var added = found.Count(l => unique.TryAdd(l.ExternalId, l));
                    logger.LogInformation("  {Category} @ Praha, 3 dny: {Found} nalezeno, {Added} nových", category, found.Count, added);
                }

                logger.LogInformation("Celkem unikátních nabídek: {Count}", unique.Count);
                foreach (var l in unique.Values)
                    logger.LogInformation("{Id} | {Title} | {Company} | {Location} | {Tags}",
                        l.ExternalId, l.Title, l.Company, l.Location, string.Join(", ", l.Tags));
                return;
            }
            
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

            // 2) each job vs. the profile
            var jobsDir = Path.Combine(dataDir, "jobs");
            var jobFiles = Directory.Exists(jobsDir)
                ? Directory.GetFiles(jobsDir, "*.md").Order().ToList()
                : [];
            logger.LogInformation("Nabídek k hodnocení: {Count}", jobFiles.Count);

            foreach (var jobFile in jobFiles)
            {
                var jobText = await File.ReadAllTextAsync(jobFile, stoppingToken);
                var result = await evaluator.EvaluateAsync(analyzed.Profile, jobText, stoppingToken);

                LogEvaluation(Path.GetFileName(jobFile), result.Evaluation);
                LogTokens(Path.GetFileName(jobFile), result.Response);
            }

            logger.LogInformation("Celkem tokenů: in={In}, out={Out}", _inputTokens, _outputTokens);
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
