using JobScout.Application.Evaluation;
using JobScout.Application.Llm;
using JobScout.Application.Notifications;
using JobScout.Application.Profiles;
using System.Diagnostics;
using JobScout.Application.Reports;
using JobScout.Infrastructure.Reports;
using JobScout.Application.Scouting;
using JobScout.Application.Users;
using System.Globalization;
using System.Text;
using JobScout.Application.Scouting;
using JobScout.Core.Domain;

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
    IConfiguration config,
    IHostEnvironment env,
    IHostApplicationLifetime lifetime,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (config["Scout:Mode"] == "invite")
            {
                await CreateInvitationAsync(stoppingToken);
                return;
            }
            if (config["Scout:Mode"] == "daily")
            {
                await DailyAsync(stoppingToken);
                return;
            }
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
                logger.LogInformation("Nová verze profilu v{Version} pro dev:{Subject}", analyzed.Version.Version,
                    subject);
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

    /// <summary>Daily pipeline (RunScout) → detailed log → morning report.</summary>
    private async Task SearchAsync(IServiceProvider services, AnalyzedProfile analyzed, string subject,
        CancellationToken ct)
    {
        var result = await services.GetRequiredService<RunScout>().ExecuteAsync(analyzed, ct);
        LogDetails(result, subject);

        try
        {
            var report = await services.GetRequiredService<BuildMorningReport>().ExecuteAsync(
                analyzed.User.Id, subject, result.Run.StartedAt,
                new ReportStats(result.Recorded.Found, result.Recorded.NewForUser.Count,
                    result.Evaluation.Evaluated.Count, result.Letters.Letters.Count, result.Cost.TotalUsd),
                ct);
            WriteReport(report);

            // The morning e-mail (7f) – only real users with a mailbox who didn't turn it off, only when something is new
            if (await services.GetRequiredService<Notifier>().MorningAsync(analyzed.User, report, ct))
                logger.LogInformation("Ranní e-mail odeslán");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The run already succeeded and the results are in the DB – a broken report only warns
            logger.LogWarning(ex, "Ranní report se nepodařilo vytvořit");
        }
    }

    private void LogDetails(ScoutRunResult r, string subject)
    {
        foreach (var job in r.Recorded.NewForUser)
            logger.LogInformation("  NOVÁ pro dev:{Subject} {Id} | {Title} | {Company} | {Location}",
                subject, job.Offer.ExternalId, job.Offer.Title, job.Offer.Company, job.Offer.Location);

        foreach (var job in r.Triage.Jobs.OrderByDescending(j => j.Relevance))
            logger.LogInformation("  {Mark} {Relevance} {Title} | {Company}{Reason}",
                job.Keep ? "✔" : "✘", job.Relevance, job.Offer.Title, job.Offer.Company,
                job.Keep ? "" : $" – {job.Reason}");

        foreach (var job in r.Evaluation.Evaluated.OrderByDescending(j => j.Evaluation.Score))
            logger.LogInformation("  {Score}/10  {Title} | {Company} – {Summary}",
                job.Evaluation.Score, job.Offer.Title, job.Offer.Company, job.Evaluation.Summary);

        if (r.Letters.Failed > 0)
            logger.LogWarning("Některé dopisy byly useknuté nebo prázdné – zkusí se znovu při dalším běhu");
        foreach (var l in r.Letters.Letters)
            logger.LogInformation("""
                                  ── DOPIS: {Title} | {Company} ({Score}/10) ──
                                  {Letter}
                                  """, l.Offer.Title, l.Offer.Company, l.Match.Score, l.Letter);

        foreach (var line in r.Cost.Lines)
            logger.LogInformation("  {Purpose}: {Calls}× (chyb {Failed}), in={In}, out={Out}, ${Cost:0.0000}",
                line.Purpose, line.Calls, line.Failed, line.InputTokens, line.OutputTokens,
                line.CostMicroUsd / 1_000_000m);
        logger.LogInformation("Náklady běhu: ${Total:0.000}", r.Cost.TotalUsd);
    }

    /// <summary>Writes a dated report + "latest-{subject}.html" (always the newest) to Scout:ReportDir.</summary>
    private void WriteReport(MorningReport report)
    {
        var dir = config["Scout:ReportDir"] ?? Path.Combine(AppContext.BaseDirectory, "reports");
        Directory.CreateDirectory(dir);

        var html = HtmlReportRenderer.Render(report);
        var dated = Path.Combine(dir,
            $"report-{report.Subject}-{report.GeneratedAt.ToLocalTime():yyyy-MM-dd-HHmm}.html");
        var latest = Path.Combine(dir, $"latest-{report.Subject}.html");
        File.WriteAllText(dated, html);
        File.WriteAllText(latest, html);
        logger.LogInformation("Ranní report: {Path}", latest);

        if (config.GetValue<bool>("Scout:OpenReport"))
            Process.Start(new ProcessStartInfo(latest) { UseShellExecute = true }); // opens in the default browser
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
        }
    }

    // Relative path = inside the project (samples in the repo); absolute path = own data outside the repo
    private string ResolveDataDir(string? persona)
    {
        var configured = persona is null
            ? config["Scout:DataDir"] ?? "samples"
            : Path.Combine("samples", "personas", persona);
        var path = Path.GetFullPath(Path.Combine(env.ContentRootPath, configured));
        var source = Path.IsPathRooted(configured)
            ? "PRIVATE (vlastní data mimo repo)"
            : "SAMPLE (ukázková data z repa)";

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
        logger.LogInformation("CV: {File} ({MediaType}, {Kb} kB)", Path.GetFileName(path), mediaType,
            content.Length / 1024);

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
            pref.RemoteWork, string.Join("/", pref.AcceptableRemoteWork), string.Join("/", pref.EmploymentTypes),
            pref.MinMonthlySalaryCzk,
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

    /// <summary>Scout:Mode=invite – a one-time invitation link (ADR-0011). Shown once; only the hash is stored.</summary>
    private async Task CreateInvitationAsync(CancellationToken ct)
    {
        var note = config["Scout:InviteNote"] ?? "";
        var baseUrl = (config["Scout:PublicUrl"] ?? "https://localhost:7283").TrimEnd('/');

        await using var scope = scopeFactory.CreateAsyncScope();
        var created = await scope.ServiceProvider.GetRequiredService<CreateInvitation>().ExecuteAsync(note, ct);

        var google = $"{baseUrl}/api/auth/login/google?invite={created.Code}&returnUrl=/api/me";
        var microsoft = $"{baseUrl}/api/auth/login/microsoft?invite={created.Code}&returnUrl=/api/me";
        logger.LogInformation("""
                              ── POZVÁNKA ({Note}) – platí do {Expires:d. M. yyyy} ──
                              Google:    {Google}
                              Microsoft: {Microsoft}
                              Odkaz se už nikde neukáže – v DB je jen otisk kódu.
                              """, note, created.ExpiresAt.ToLocalTime(), google, microsoft);
    }

    /// <summary>
    /// Scout:Mode=daily – the run for every approved user with a profile (the cloud job, phase 7).
    /// The job wakes up every night hour; NightSchedule says whether this is tonight's run, the retry an hour later
    /// (only users whose run failed – a second failure tells the admins), or nothing. Started by hand in the daytime,
    /// it runs for everybody as before. One scope per user: one failure can't poison the next user's data.
    /// </summary>
    private async Task DailyAsync(CancellationToken ct)
    {
        var includeDev = config.GetValue<bool>("Scout:IncludeDevUsers");
        var now = TimeProvider.System.GetUtcNow();
        var kind = NightSchedule.Decide(now);
        var today = NightSchedule.Today(now);
        if (kind == NightRun.NotNow)
        {
            logger.LogInformation("Dnes v noci se hledá v {Hour}:00 – teď nic", NightSchedule.HourFor(today));
            return;
        }
        logger.LogInformation("Běh: {Kind} (dnešní noční hodina {Hour}:00)", kind, NightSchedule.HourFor(today));

        // The demo account starts every day clean – yesterday's visitors' statuses and notes go away (ADR-0020)
        if (kind != NightRun.Retry)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var reset = await scope.ServiceProvider.GetRequiredService<JobScout.Application.Demo.ResetDemo>().ExecuteAsync(ct);
            if (reset > 0) logger.LogInformation("Ukázkový účet: vráceno do výchozího stavu {Count} nabídek", reset);
        }

        IReadOnlyList<Guid> userIds;
        await using (var scope = scopeFactory.CreateAsyncScope())
            userIds = await scope.ServiceProvider.GetRequiredService<ListScoutUsers>().ExecuteAsync(includeDev, ct,
                withoutSuccessSince: kind == NightRun.Retry ? NightSchedule.PlannedStart(today) : null);
        logger.LogInformation("{Kind} pro {Count} uživatelů{Dev}", kind == NightRun.Retry ? "Opakovaný pokus" : "Ranní běh",
            userIds.Count, includeDev ? " (včetně dev person)" : "");

        var failed = 0;
        string? firstError = null;
        foreach (var userId in userIds)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            try
            {
                var prepared = await scope.ServiceProvider.GetRequiredService<PrepareScoutProfile>().ExecuteAsync(userId, ct);
                if (prepared is null)
                    continue;

                var subject = ReportSubject(prepared.User);
                logger.LogInformation("══ {Subject} ({Provider}) – profil v{Version}{New} ══",
                    prepared.User.DisplayName, prepared.User.Provider, prepared.Version.Version,
                    prepared.IsNew ? ", nově přeanalyzovaný" : "");

                await SearchAsync(scope.ServiceProvider, prepared, subject, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                firstError ??= ex.Message;
                logger.LogError(ex, "Běh pro uživatele {UserId} selhal – pokračuji dalším", userId);
            }
        }

        logger.LogInformation("Ranní běh hotový: {Ok} v pořádku, {Failed} selhalo", userIds.Count - failed, failed);
        if (failed > 0)
        {
            Environment.ExitCode = 1;
            // Tonight's first attempt stays quiet – the retry an hour later tries again and only then tells the admins
            if (kind == NightRun.Main)
            {
                logger.LogWarning("Pro {Failed} uživatelů to zkusíme znovu za hodinu", failed);
                return;
            }
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<Notifier>()
                .MorningRunFailedAsync(failed, userIds.Count, firstError ?? "", ct);
        }
    }

    /// <summary>File-name-safe report name: "google-stanislav-matzke".</summary>
    private static string ReportSubject(User user)
    {
        var name = new string(user.DisplayName.ToLowerInvariant()
                .Normalize(NormalizationForm.FormD)                       // "š" → "s" + háček …
                .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)   // … háček pryč
                .Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')
                .ToArray())
            .Trim('-');
        return $"{user.Provider}-{(name.Length > 0 ? name : user.Id.ToString("N")[..8])}";
    }
}
