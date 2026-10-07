using System.Net;
using JobScout.Application.Evaluation;
using JobScout.Application.Llm;
using JobScout.Core.Domain;
using JobScout.Core.Jobs;
using JobScout.Tests.TestUtils;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Tests.Application.Evaluation;

/// <summary>
/// Shortlisted jobs → detail (fetched once, shared by all users) → full evaluation (Sonnet) → Evaluated.
/// Limited per run; a removed ad is triaged out, a temporary failure waits for the next run.
/// </summary>
public sealed class EvaluateJobsTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 8, 6, 0, 0, TimeSpan.Zero);

    private const string EvaluationJson = """
        { "score": 8, "summary": "Stavbyvedoucí na rodinných domech, sedí.", "reasons": ["Pozemní stavby"], "risks": [],
          "questions": ["Jaká je mzda?"], "salary": "", "remoteWork": "onsite", "employmentType": "employee" }
        """;

    private readonly SqliteTestDatabase _database = new();
    private readonly FixedTimeProvider _time = new(T0);
    private readonly FakeJobSource _jobsCz = new("jobs.cz");
    private readonly FakeLlmClient _llm = new(EvaluationJson);
    private readonly Guid _userId;
    private readonly Guid _otherUserId;
    private int _nextId;

    public EvaluateJobsTests()
    {
        using var db = _database.CreateContext();
        var user = new User("dev", "zednik", "", "zednik", T0);
        var other = new User("dev", "autolakyrnik", "", "autolakyrnik", T0);
        db.AddRange(user, other);
        db.SaveChanges();
        (_userId, _otherUserId) = (user.Id, other.Id);
    }

    public void Dispose() => _database.Dispose();

    // ---------- helpers ----------

    /// <summary>Creates an offer + a match in the given stage; registers a detail on the fake portal unless told otherwise.</summary>
    private string Seed(string title, MatchStage stage = MatchStage.Shortlisted, Guid? userId = null,
        bool withDetail = true, bool completeDetail = true, string source = "jobs.cz", string? externalId = null)
    {
        var id = externalId ?? $"2001{++_nextId:D6}";
        using var db = _database.CreateContext();

        var offer = db.JobOffers.SingleOrDefault(o => o.Source == source && o.ExternalId == id)
                    ?? JobOffer.FromListing(new JobListing(source, id, title, "Stavby s.r.o.", "Opava",
                        $"https://www.jobs.cz/rpd/{id}/", ["Plný úvazek"]), T0);
        if (db.Entry(offer).State == EntityState.Detached)
            db.JobOffers.Add(offer);

        var match = new JobMatch(userId ?? _userId, offer.Id, 2, T0);
        if (stage == MatchStage.Shortlisted) match.Shortlist(T0);
        if (stage == MatchStage.TriagedOut) match.MarkTriagedOut("mimo profil", T0);
        if (stage == MatchStage.Evaluated) match.ApplyEvaluation(5, "{}", T0);
        db.JobMatches.Add(match);
        db.SaveChanges();

        if (withDetail)
            _jobsCz.Details[id] = new JobDetail(source, id, offer.Url, title, offer.Company,
                $"Náplň práce: {title}, rodinné domy, Opava.", completeDetail);
        return id;
    }

    private async Task<EvaluateJobsResult> EvaluateAsync(Guid? userId = null, FakeLlmClient? llm = null, int? maxPerRun = null)
    {
        await using var db = _database.CreateContext();
        var useCase = new EvaluateJobs(db, [_jobsCz], new JobEvaluator(llm ?? _llm), _time);
        return await useCase.ExecuteAsync(userId ?? _userId, TestProfiles.With(["Zedník", "Stavbyvedoucí"], "Opava", 40),
            maxPerRun ?? EvaluateJobs.MaxPerRun);
    }

    private async Task<JobMatch> MatchAsync(string externalId, Guid? userId = null)
    {
        await using var db = _database.CreateContext();
        return await db.JobMatches.Include(m => m.JobOffer)
            .SingleAsync(m => m.JobOffer!.ExternalId == externalId && m.UserId == (userId ?? _userId));
    }

    // ---------- happy path ----------

    [Fact]
    public async Task Shortlisted_job_gets_detail_and_evaluation()
    {
        var id = Seed("Stavbyvedoucí");

        var result = await EvaluateAsync();

        var job = Assert.Single(result.Evaluated);
        Assert.Equal(8, job.Evaluation.Score);
        var match = await MatchAsync(id);
        Assert.Equal(MatchStage.Evaluated, match.Stage);
        Assert.Equal(8, match.Score);
        Assert.Contains("\"score\":8", match.EvaluationJson);
        Assert.True(match.IsVisible);                                   // now the user sees it
        Assert.Equal("Náplň práce: Stavbyvedoucí, rodinné domy, Opava.", match.JobOffer!.DetailText);
        Assert.True(match.JobOffer.DetailIsComplete);
        Assert.Equal(T0, match.JobOffer.DetailFetchedAt);
        Assert.Equal(1, result.DetailsFetched);
    }

    [Fact]
    public async Task Evaluation_prompt_contains_listing_and_detail_text()
    {
        Seed("Stavbyvedoucí");

        await EvaluateAsync();

        var prompt = Assert.Single(_llm.Requests).UserPrompt;
        Assert.Contains("Stavbyvedoucí", prompt);
        Assert.Contains("Stavby s.r.o.", prompt);
        Assert.Contains("rodinné domy, Opava", prompt);
        Assert.DoesNotContain("neúplný", prompt);
    }

    [Fact]
    public async Task Incomplete_detail_is_flagged_for_the_model()
    {
        // No job may silently disappear – an incomplete ad is evaluated, but the model must know it's a snippet
        Seed("Stavbyvedoucí", completeDetail: false);

        await EvaluateAsync();

        Assert.Contains("neúplný", Assert.Single(_llm.Requests).UserPrompt);
    }

    // ---------- detail is shared ----------

    [Fact]
    public async Task Detail_already_stored_is_not_fetched_again_for_another_user()
    {
        var id = Seed("Stavbyvedoucí");
        await EvaluateAsync();

        Seed("Stavbyvedoucí", userId: _otherUserId, externalId: id);   // the same ad, another user
        var other = await EvaluateAsync(_otherUserId);

        Assert.Single(_jobsCz.DetailRequests);                          // fetched once for everybody
        Assert.Equal(0, other.DetailsFetched);
        Assert.Single(other.Evaluated);                                // but evaluated for him personally
    }

    // ---------- what gets evaluated ----------

    [Fact]
    public async Task Only_shortlisted_jobs_of_this_user_are_evaluated()
    {
        Seed("Čeká na hodnocení");
        Seed("Nově nalezená", MatchStage.Found);
        Seed("Vyřazená", MatchStage.TriagedOut);
        Seed("Už ohodnocená", MatchStage.Evaluated);
        Seed("Cizí", userId: _otherUserId);

        var result = await EvaluateAsync();

        Assert.Equal("Čeká na hodnocení", Assert.Single(result.Evaluated).Offer.Title);
        Assert.Single(_llm.Requests);
    }

    [Fact]
    public async Task Nothing_shortlisted_means_no_requests_at_all()
    {
        var result = await EvaluateAsync();

        Assert.Empty(result.Evaluated);
        Assert.Empty(_llm.Requests);
        Assert.Empty(_jobsCz.DetailRequests);
    }

    [Fact]
    public async Task Run_is_limited_and_the_rest_waits_for_the_next_run()
    {
        for (var i = 1; i <= 5; i++) Seed($"Stavbyvedoucí {i}");

        var first = await EvaluateAsync(maxPerRun: 3);
        var second = await EvaluateAsync(maxPerRun: 3);

        Assert.Equal(3, first.Evaluated.Count);
        Assert.Equal(2, first.Remaining);
        Assert.Equal(2, second.Evaluated.Count);
        Assert.Equal(0, second.Remaining);
        Assert.Equal(5, _llm.Requests.Count);
    }

    // ---------- failures ----------

    [Fact]
    public async Task Removed_ad_is_triaged_out_without_calling_the_ai()
    {
        var id = Seed("Stavbyvedoucí", withDetail: false);              // portal answers 404

        var result = await EvaluateAsync();

        var match = await MatchAsync(id);
        Assert.Equal(MatchStage.TriagedOut, match.Stage);
        Assert.Contains("není dostupný", match.TriageReason);
        Assert.Empty(_llm.Requests);
        Assert.Equal(1, result.Unavailable);
    }

    [Fact]
    public async Task Temporary_detail_failure_leaves_the_job_for_the_next_run_and_continues()
    {
        var broken = Seed("Stavbyvedoucí A");
        Seed("Stavbyvedoucí B");
        _jobsCz.DetailFailures[broken] = new HttpRequestException("Server error", null, HttpStatusCode.InternalServerError);

        var result = await EvaluateAsync();

        Assert.Equal(MatchStage.Shortlisted, (await MatchAsync(broken)).Stage);
        Assert.Equal("Stavbyvedoucí B", Assert.Single(result.Evaluated).Offer.Title);
        Assert.Equal(1, result.Remaining);
    }

    [Fact]
    public async Task Job_from_an_unknown_source_waits_without_crashing()
    {
        // e.g. a source that was switched off in configuration
        Seed("Zedník", source: "prace.cz");

        var result = await EvaluateAsync();

        Assert.Empty(result.Evaluated);
        Assert.Equal(1, result.Remaining);
        Assert.Empty(_llm.Requests);
    }

    [Fact]
    public async Task Ai_failure_stops_the_run_but_keeps_what_was_evaluated()
    {
        var first = Seed("Stavbyvedoucí 1");
        var second = Seed("Stavbyvedoucí 2");
        var calls = 0;
        var llm = new FakeLlmClient(_ => ++calls == 1
            ? new LlmResponse(EvaluationJson, "fake-sonnet", "end_turn", 3000, 700)
            : throw new HttpRequestException("API nedostupné"));

        await Assert.ThrowsAsync<HttpRequestException>(() => EvaluateAsync(llm: llm));

        var stages = new[] { (await MatchAsync(first)).Stage, (await MatchAsync(second)).Stage };
        Assert.Contains(MatchStage.Evaluated, stages);      // saved after each job
        Assert.Contains(MatchStage.Shortlisted, stages);    // the other one waits for the next run
    }
}
