using JobScout.Core.Evaluation;
using JobScout.Core.Jobs;
using JobScout.Core.Llm;
using JobScout.Core.Profile;

namespace JobScout.Worker;

/// <summary>
/// One-shot run: CV + notes → profile, then every job in the data folder → evaluation.
/// Data folder layout: cv.pdf | cv.md, preferences.md (optional), jobs/*.md
/// </summary>
public class Worker(
    ProfileExtractor profileExtractor,
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
            if (config["Scout:Mode"] == "search")
            {
                var listings = await jobSource.SearchAsync(new JobSearchQuery(".NET architekt", "praha"), stoppingToken);
                foreach (var l in listings)
                    logger.LogInformation("{Id} | {Title} | {Company} | {Location} | {Tags}",
                        l.ExternalId, l.Title, l.Company, l.Location, string.Join(", ", l.Tags));
                
                foreach (var l in listings.Take(3))
                {
                    var d = await jobSource.GetDetailAsync(l.ExternalId, stoppingToken);
                    logger.LogInformation("── DETAIL {Id} (úplný: {Complete}) ──\n{Text}",
                        d.ExternalId, d.IsComplete, d.Text.Length > 1500 ? d.Text[..1500] + "…" : d.Text);
                }    
                
                return; // finally stejně zastaví aplikaci
            }
            
            var dataDir = ResolveDataDir();

            // 1) CV + notes → structured profile (in the app: once per CV upload, not per job)
            var cv = await LoadCvAsync(dataDir, stoppingToken);
            var notes = await ReadOptionalAsync(Path.Combine(dataDir, "preferences.md"), stoppingToken);

            var extraction = await profileExtractor.ExtractAsync(cv, notes, stoppingToken);
            LogProfile(extraction.Profile);
            LogTokens("profil", extraction.Response);

            // 2) each job vs. the profile
            var jobFiles = Directory.GetFiles(Path.Combine(dataDir, "jobs"), "*.md").Order().ToList();
            logger.LogInformation("Nabídek k hodnocení: {Count}", jobFiles.Count);

            foreach (var jobFile in jobFiles)
            {
                var jobText = await File.ReadAllTextAsync(jobFile, stoppingToken);
                var result = await evaluator.EvaluateAsync(extraction.Profile, jobText, stoppingToken);

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
    private string ResolveDataDir()
    {
        var configured = config["Scout:DataDir"] ?? "samples";
        var path = Path.GetFullPath(Path.Combine(env.ContentRootPath, configured));
        var source = Path.IsPathRooted(configured) ? "PRIVATE (vlastní data mimo repo)" : "SAMPLE (ukázková data z repa)";

        logger.LogInformation("Zdroj dat: {Source} – {Path}", source, path);

        return Directory.Exists(path)
            ? path
            : throw new DirectoryNotFoundException($"Složka s daty neexistuje: {path}");
    }

    private async Task<LlmDocument> LoadCvAsync(string dataDir, CancellationToken ct)
    {
        var pdf = Path.Combine(dataDir, "cv.pdf");
        var md = Path.Combine(dataDir, "cv.md");

        var (path, mediaType) = File.Exists(pdf) ? (pdf, "application/pdf")
            : File.Exists(md) ? (md, "text/plain")
            : throw new FileNotFoundException($"Ve složce {dataDir} chybí cv.pdf nebo cv.md");

        var content = await File.ReadAllBytesAsync(path, ct);
        logger.LogInformation("CV: {File} ({MediaType}, {Kb} kB)", Path.GetFileName(path), mediaType, content.Length / 1024);

        return new LlmDocument("CV", mediaType, content);
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
        logger.LogInformation("""
            ── PROFIL ──
            {Headline}
            Praxe: {Years} let, {Seniority}
            Cílové role: {Roles}
            Hlavní stack: {Core}
            Další: {Other}
            Obory: {Domains} | Jazyky: {Languages}
            Preference: HO {Remote} (přijatelné: {Acceptable}), forma {Types}, min. mzda {Salary} Kč, lokality: {Locations}
            Priority: {Priorities}
            Vyhnout se: {Avoid}
            """,
            p.Headline, p.YearsOfExperience, p.Seniority,
            string.Join(", ", p.TargetRoles), string.Join(", ", p.CoreSkills), string.Join(", ", p.OtherSkills),
            string.Join(", ", p.Domains), string.Join(", ", p.Languages),
            pref.RemoteWork, string.Join("/", pref.AcceptableRemoteWork), string.Join("/", pref.EmploymentTypes), pref.MinMonthlySalaryCzk, string.Join(", ", pref.Locations),
            string.Join(" > ", pref.Priorities), string.Join(", ", pref.Avoid));
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
