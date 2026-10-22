using JobScout.Application.Evaluation;
using JobScout.Application.Jobs;
using JobScout.Application.Letters;
using JobScout.Application.Llm;
using JobScout.Application.Profiles;
using JobScout.Application.Scouting;
using JobScout.Application.Triage;
using JobScout.Core.Domain;
using JobScout.Core.Jobs;
using JobScout.Tests.TestUtils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobScout.Tests.Application.Scouting;

/// <summary>
/// The whole daily pipeline for one user, wired from the real use cases over a real (in-memory SQLite) database –
/// only the job portal and the AI are fake. Details of each step are tested in its own test class;
/// here we check the glue: order, windows, de-duplication across searches and the run journal.
/// </summary>
public sealed class RunScoutTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 12, 5, 0, 0, TimeSpan.Zero);   // Monday 7:00 in Prague

    private const string Suggestions = """
        { "mappings": [ { "profession": "Stavbyvedoucí", "labels": ["Stavbyvedoucí"] } ] }
        """;

    /// <summary>No decisions = every job is "unsure" (relevance 3) and stays – see TriageJobs.</summary>
    private const string KeepAll = """{ "decisions": [] }""";

    private const string EvaluationJson = """
        { "score": 8, "summary": "Stavbyvedoucí na rodinných domech, sedí.", "reasons": ["Pozemní stavby"], "risks": [],
          "questions": ["Jaká je mzda?"], "salary": "", "remoteWork": "onsite", "employmentType": "employee" }
        """;

    private const string Letter = "Dobrý den,\n\nreaguji na Vaši nabídku…\n\nS pozdravem\n[Jméno a příjmení]";

    /// <summary>Home Kolín + 30 km and Praha → two searches per run.</summary>
    private static readonly CandidateProfile Profile = TestProfiles.With(["Stavbyvedoucí"], extraLocations: ["Praha"]);

    private readonly SqliteTestDatabase _database = new();
    private readonly FixedTimeProvider _time = new(T0);
    private readonly FakeLlmClient _llm = new(Respond);
    private readonly FakeJobSource _jobsCz = new("jobs.cz");
    private readonly Guid _userId;
    private readonly Guid _profileId;
    private int _nextId;

    public RunScoutTests()
    {
        _jobsCz.Existing["Stavbyvedoucí"] = "stavbyvedouci";

        using var db = _database.CreateContext();
        var user = new User("dev", "zednik", "", "zednik", T0);
        var version = new UserProfile(user.Id, 1, "cv.md", null, "HASH", "{}", "fake-model", T0);
        db.AddRange(user, version);
        db.SaveChanges();
        (_userId, _profileId) = (user.Id, version.Id);
    }

    public void Dispose() => _database.Dispose();

    // ---------- helpers ----------

    private static LlmResponse Respond(LlmRequest request) => new(
        request.Purpose switch
        {
            LlmPurpose.SearchPlan => Suggestions,
            LlmPurpose.Triage => KeepAll,
            LlmPurpose.Evaluation => EvaluationJson,
            LlmPurpose.CoverLetter => Letter,
            _ => throw new InvalidOperationException($"Unexpected AI call: {request.Purpose}")
        },
        "fake-model", "end_turn", InputTokens: 100, OutputTokens: 50);

    /// <summary>An ad the fake portal returns in every search, with its detail page.</summary>
    private string Publish(string title)
    {
        var id = $"3001{++_nextId:D6}";
        var url = $"https://www.jobs.cz/rpd/{id}/";
        _jobsCz.Listings.Add(new JobListing("jobs.cz", id, title, "Stavby s.r.o.", "Kolín", url, ["Plný úvazek"]));
        _jobsCz.Details[id] = new JobDetail("jobs.cz", id, url, title, "Stavby s.r.o.",
            $"Hledáme {title}. Pozemní stavby, rodinné domy, Kolín a okolí.", true);
        return id;
    }

    /// <summary>One run = one DbContext (like one Worker scope), wired from the real use cases.</summary>
    private async Task<ScoutRunResult> RunAsync()
    {
        await using var db = _database.CreateContext();
        var user = await db.Users.SingleAsync(u => u.Id == _userId);
        var version = await db.UserProfiles.SingleAsync(p => p.Id == _profileId);
        IJobSource[] sources = [_jobsCz];

        var useCase = new RunScout(
            new PlanSearch(db, new CategoryResolver(db, _time), new CategorySuggester(_llm), sources),
            new ScoutRunJournal(db, _time),
            sources,
            new RecordFoundJobs(db, _time),
            new TriageJobs(db, _llm, _time),
            new EvaluateJobs(db, new MatchEvaluator(sources, new JobEvaluator(_llm), _time)),
            new WriteCoverLetters(db, new CoverLetterWriter(_llm), _time),
            new UsageReport(db),
            NullLogger<RunScout>.Instance);

        return await useCase.ExecuteAsync(new AnalyzedProfile(user, version, Profile, Response: null), CancellationToken.None);
    }

    private int AiCalls(LlmPurpose purpose) => _llm.Requests.Count(r => r.Purpose == purpose);

    // ---------- the happy path ----------

    [Fact]
    public async Task Run_goes_from_search_to_cover_letters_and_succeeds()
    {
        Publish("Stavbyvedoucí – rodinné domy");
        Publish("Stavbyvedoucí – bytové domy");

        var result = await RunAsync();

        Assert.True(result.PlanIsNew);
        Assert.Equal(2, result.Recorded.NewForUser.Count);
        Assert.Equal(2, result.Triage.Kept);
        Assert.Equal(2, result.Evaluation.Evaluated.Count);
        Assert.Equal(2, result.Letters.Letters.Count);

        await using var db = _database.CreateContext();
        var run = await db.ScoutRuns.SingleAsync();
        Assert.Equal(ScoutRunStatus.Succeeded, run.Status);
        Assert.Equal(result.Run.Id, run.Id);
        Assert.Equal(2, run.Found);
        Assert.Equal(2, run.NewForUser);
    }

    [Fact]
    public async Task Same_ad_found_by_two_searches_is_recorded_and_evaluated_once()
    {
        Publish("Stavbyvedoucí");

        var result = await RunAsync();

        Assert.Equal(4, result.Searches.Count);                         // category and title text × Kolín + 30 km, Praha
        Assert.All(result.Searches, s => Assert.Equal(1, s.Found));
        Assert.Equal(new[] { 1, 0, 0, 0 }, result.Searches.Select(s => s.Added));   // the others only repeated it
        Assert.Equal(1, result.Recorded.Found);
        Assert.Equal(1, AiCalls(LlmPurpose.Evaluation));
    }

    // ---------- windows and repeated runs ----------

    [Fact]
    public async Task First_run_searches_three_days_next_morning_only_the_last_day()
    {
        await RunAsync();
        Assert.All(_jobsCz.SearchQueries, q => Assert.Equal(JobAge.ThreeDays, q.MaxAge));

        _jobsCz.SearchQueries.Clear();
        _time.Now = T0.AddDays(1);
        var second = await RunAsync();

        Assert.Equal(JobAge.Day, second.Window.MaxAge);
        Assert.All(_jobsCz.SearchQueries, q => Assert.Equal(JobAge.Day, q.MaxAge));
        Assert.NotEmpty(_jobsCz.SearchQueries);
    }

    [Fact]
    public async Task Next_run_reuses_the_plan_and_does_not_pay_again_for_seen_ads()
    {
        Publish("Stavbyvedoucí");
        await RunAsync();

        _time.Now = T0.AddDays(1);
        var second = await RunAsync();

        Assert.False(second.PlanIsNew);
        Assert.Empty(second.Recorded.NewForUser);
        Assert.Equal(1, AiCalls(LlmPurpose.SearchPlan));      // plan built once per profile version
        Assert.Equal(1, AiCalls(LlmPurpose.Triage));          // nothing new → no triage call
        Assert.Equal(1, AiCalls(LlmPurpose.Evaluation));
        Assert.Equal(1, AiCalls(LlmPurpose.CoverLetter));     // a letter is never rewritten
    }

    // ---------- failures ----------

    [Fact]
    public async Task Failing_source_marks_the_run_failed_and_rethrows()
    {
        _jobsCz.SearchFailure = new HttpRequestException("Service Unavailable (503)");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(RunAsync);

        Assert.Contains("503", ex.Message);
        await using var db = _database.CreateContext();
        var run = await db.ScoutRuns.SingleAsync();
        Assert.Equal(ScoutRunStatus.Failed, run.Status);
        Assert.Contains("503", run.Error);
        Assert.Equal(0, AiCalls(LlmPurpose.Triage));
    }

    [Fact]
    public async Task After_a_failed_run_the_next_one_still_searches_three_days()
    {
        await RunAsync();                                          // Monday – succeeded
        _time.Now = T0.AddDays(1);
        _jobsCz.SearchFailure = new HttpRequestException("503");
        await Assert.ThrowsAsync<HttpRequestException>(RunAsync);  // Tuesday – portal down

        _jobsCz.SearchFailure = null;
        _jobsCz.SearchQueries.Clear();
        _time.Now = T0.AddDays(2);
        var wednesday = await RunAsync();

        Assert.Equal(JobAge.ThreeDays, wednesday.Window.MaxAge);  // Tuesday's ads are not lost
    }
}
