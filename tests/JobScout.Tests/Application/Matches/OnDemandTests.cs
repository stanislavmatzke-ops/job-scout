using System.Net;
using JobScout.Application.Evaluation;
using JobScout.Application.Letters;
using JobScout.Application.Llm;
using JobScout.Application.Matches;
using JobScout.Core.Domain;
using JobScout.Core.Jobs;
using JobScout.Tests.TestUtils;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Tests.Application.Matches;

/// <summary>
/// "AI pre-selects, the human decides": the user can evaluate ANY job of theirs (waiting, triaged out, or again)
/// and have a letter written for any evaluated job, whatever the score. The same building blocks as the morning run.
/// </summary>
public sealed class OnDemandTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 10, 6, 0, 0, TimeSpan.Zero);
    private const string Letter = "Dobrý den,\n\nreaguji na Vaši nabídku…\n\nS pozdravem\n[Jméno a příjmení]";

    private readonly SqliteTestDatabase _database = new();
    private readonly FixedTimeProvider _time = new(T0.AddDays(1));
    private readonly FakeJobSource _jobsCz = new("jobs.cz");
    private readonly List<LlmCallContext.Scope?> _billedTo = [];
    private readonly FakeLlmClient _llm;
    private readonly Guid _userId;
    private readonly Guid _otherUserId;
    private string _letterStopReason = "end_turn";
    private string _letterText = Letter;

    public OnDemandTests()
    {
        _llm = new FakeLlmClient(request =>
        {
            _billedTo.Add(LlmCallContext.Current);
            return request.Purpose == LlmPurpose.CoverLetter
                ? new LlmResponse(_letterText, "fake-model", _letterStopReason, InputTokens: 100, OutputTokens: 50)
                : new LlmResponse(MatchSeeder.EvaluationJson(6, "Částečná shoda."), "fake-model", "end_turn", InputTokens: 100, OutputTokens: 50);
        });

        using var db = _database.CreateContext();
        var user = new User("dev", "private", "", "private", T0);
        var other = new User("dev", "zednik", "", "zednik", T0);
        db.AddRange(user, other);
        db.SaveChanges();
        (_userId, _otherUserId) = (user.Id, other.Id);
        ProfileSeeder.Add(db, _userId, T0);
    }

    public void Dispose() => _database.Dispose();

    // ---------- helpers ----------

    private Guid Seed(MatchStage stage, int score = 7, string? letter = null, bool withAdText = false, Guid? userId = null)
    {
        using var db = _database.CreateContext();
        var id = MatchSeeder.Add(db, userId ?? _userId, "Solution Architect", T0, stage, score, triage: 2, letter: letter,
            adText: withAdText ? "Hledáme architekta, hybrid Praha." : null);
        // the fake portal knows the ad unless a test says otherwise
        var offer = db.JobMatches.Include(m => m.JobOffer).Single(m => m.Id == id).JobOffer!;
        _jobsCz.Details[offer.ExternalId] = new JobDetail("jobs.cz", offer.ExternalId, offer.Url, offer.Title, offer.Company,
            "Hledáme architekta, hybrid Praha.", true);
        return id;
    }

    private string ExternalIdOf(Guid matchId)
    {
        using var db = _database.CreateContext();
        return db.JobMatches.Include(m => m.JobOffer).Single(m => m.Id == matchId).JobOffer!.ExternalId;
    }

    private JobMatch Stored(Guid matchId)
    {
        using var db = _database.CreateContext();
        return db.JobMatches.Include(m => m.JobOffer).Single(m => m.Id == matchId);
    }

    private async Task<EvaluateNowResult> EvaluateAsync(Guid matchId, Guid? asUser = null)
    {
        await using var db = _database.CreateContext();
        var evaluator = new MatchEvaluator([_jobsCz], new JobEvaluator(_llm), _time);
        return await new EvaluateMatchNow(db, evaluator).ExecuteAsync(asUser ?? _userId, matchId);
    }

    private async Task<WriteLetterResult> WriteLetterAsync(Guid matchId, bool replace = false, Guid? asUser = null)
    {
        await using var db = _database.CreateContext();
        return await new WriteCoverLetterNow(db, new CoverLetterWriter(_llm), _time)
            .ExecuteAsync(asUser ?? _userId, matchId, replace);
    }

    private async Task<bool> EditLetterAsync(Guid matchId, string? text, Guid? asUser = null)
    {
        await using var db = _database.CreateContext();
        return await new UpdateCoverLetter(db, _time).ExecuteAsync(asUser ?? _userId, matchId, text);
    }

    private int AiCalls(LlmPurpose purpose) => _llm.Requests.Count(r => r.Purpose == purpose);

    // ---------- evaluate now ----------

    [Fact]
    public async Task Waiting_job_is_evaluated_on_demand()
    {
        var id = Seed(MatchStage.Shortlisted);   // relevance 2 – the morning run leaves it for the user

        var result = await EvaluateAsync(id);

        Assert.Equal(EvaluateNowStatus.Evaluated, result.Status);
        Assert.Equal(6, result.Evaluation!.Score);
        var match = Stored(id);
        Assert.Equal(MatchStage.Evaluated, match.Stage);
        Assert.Equal(6, match.Score);
        Assert.NotNull(match.JobOffer!.DetailText);                 // detail fetched on the way
    }

    [Fact]
    public async Task Triaged_out_job_can_be_evaluated_anyway()
    {
        var id = Seed(MatchStage.TriagedOut);

        var result = await EvaluateAsync(id);

        Assert.Equal(EvaluateNowStatus.Evaluated, result.Status);
        Assert.Equal(MatchStage.Evaluated, Stored(id).Stage);
    }

    [Fact]
    public async Task Evaluated_job_can_be_evaluated_again()
    {
        var id = Seed(MatchStage.Evaluated, score: 9, withAdText: true);

        await EvaluateAsync(id);

        Assert.Equal(6, Stored(id).Score);
        Assert.Empty(_jobsCz.DetailRequests);                      // the stored ad text is reused
    }

    [Fact]
    public async Task On_demand_cost_is_billed_to_the_user_without_a_run()
    {
        var id = Seed(MatchStage.Shortlisted);

        await EvaluateAsync(id);

        var scope = Assert.Single(_billedTo);
        Assert.Equal<Guid?>(_userId, scope!.UserId);
        Assert.Null(scope.ScoutRunId);
    }

    [Fact]
    public async Task Removed_ad_is_reported_and_no_AI_is_paid()
    {
        var id = Seed(MatchStage.Shortlisted);
        _jobsCz.Details.Remove(ExternalIdOf(id));                   // 404 on the portal

        var result = await EvaluateAsync(id);

        Assert.Equal(EvaluateNowStatus.AdRemoved, result.Status);
        Assert.Equal(MatchStage.TriagedOut, Stored(id).Stage);
        Assert.Equal(0, _llm.Calls);
    }

    [Fact]
    public async Task Portal_failure_means_try_later()
    {
        var id = Seed(MatchStage.Shortlisted);
        _jobsCz.DetailFailures[ExternalIdOf(id)] = new HttpRequestException("Bad gateway", null, HttpStatusCode.BadGateway);

        var result = await EvaluateAsync(id);

        Assert.Equal(EvaluateNowStatus.TryLater, result.Status);
        Assert.Equal(MatchStage.Shortlisted, Stored(id).Stage);
        Assert.Equal(0, _llm.Calls);
    }

    [Fact]
    public async Task Without_a_profile_nothing_can_be_evaluated()
    {
        await using (var db = _database.CreateContext())
        {
            db.UserProfiles.RemoveRange(db.UserProfiles);
            await db.SaveChangesAsync();
        }
        var id = Seed(MatchStage.Shortlisted);

        Assert.Equal(EvaluateNowStatus.NoProfile, (await EvaluateAsync(id)).Status);
        Assert.Equal(0, _llm.Calls);
    }

    [Fact]
    public async Task Somebody_elses_job_is_not_found_and_costs_nothing()
    {
        var id = Seed(MatchStage.Shortlisted, userId: _otherUserId);

        Assert.Equal(EvaluateNowStatus.NotFound, (await EvaluateAsync(id)).Status);
        Assert.Equal(0, _llm.Calls);
    }

    [Fact]
    public async Task Newest_profile_version_is_used()
    {
        await using (var db = _database.CreateContext())
            ProfileSeeder.Add(db, _userId, T0.AddHours(1), version: 2, "Autolakyrnik-test");
        var id = Seed(MatchStage.Shortlisted);

        await EvaluateAsync(id);

        Assert.Contains("Autolakyrnik-test", Assert.Single(_llm.Requests).UserPrompt);
    }

    // ---------- letter now ----------

    [Fact]
    public async Task Letter_is_written_for_any_evaluated_job_whatever_the_score()
    {
        var id = Seed(MatchStage.Evaluated, score: 5, withAdText: true);   // below the automatic threshold of 7

        var result = await WriteLetterAsync(id);

        Assert.Equal(WriteLetterStatus.Written, result.Status);
        Assert.Equal(Letter, result.Letter);
        Assert.Equal(Letter, Stored(id).CoverLetter);
        Assert.Equal<Guid?>(_userId, Assert.Single(_billedTo)!.UserId);
    }

    [Fact]
    public async Task Letter_needs_an_evaluation_first()
    {
        var id = Seed(MatchStage.Shortlisted);

        Assert.Equal(WriteLetterStatus.NotEvaluated, (await WriteLetterAsync(id)).Status);
        Assert.Equal(0, _llm.Calls);
    }

    [Fact]
    public async Task Existing_letter_is_kept_unless_replace_is_asked_for()
    {
        var id = Seed(MatchStage.Evaluated, letter: "Můj upravený dopis", withAdText: true);

        var kept = await WriteLetterAsync(id);
        Assert.Equal(WriteLetterStatus.AlreadyWritten, kept.Status);
        Assert.Equal("Můj upravený dopis", Stored(id).CoverLetter);
        Assert.Equal(0, _llm.Calls);

        var replaced = await WriteLetterAsync(id, replace: true);
        Assert.Equal(WriteLetterStatus.Written, replaced.Status);
        Assert.Equal(Letter, Stored(id).CoverLetter);
    }

    [Fact]
    public async Task Cut_off_letter_is_not_saved()
    {
        var id = Seed(MatchStage.Evaluated, letter: "Původní", withAdText: true);
        _letterStopReason = "max_tokens";

        var result = await WriteLetterAsync(id, replace: true);

        Assert.Equal(WriteLetterStatus.Failed, result.Status);
        Assert.Equal("Původní", Stored(id).CoverLetter);
    }

    [Fact]
    public async Task Empty_letter_from_AI_is_not_saved()
    {
        var id = Seed(MatchStage.Evaluated, withAdText: true);
        _letterText = "   ";

        Assert.Equal(WriteLetterStatus.Failed, (await WriteLetterAsync(id)).Status);
        Assert.Null(Stored(id).CoverLetter);
    }

    [Fact]
    public async Task Letter_for_somebody_elses_job_is_not_found()
    {
        var id = Seed(MatchStage.Evaluated, userId: _otherUserId, withAdText: true);

        Assert.Equal(WriteLetterStatus.NotFound, (await WriteLetterAsync(id)).Status);
        Assert.Equal(0, _llm.Calls);
    }

    // ---------- edit the letter ----------

    [Fact]
    public async Task Edited_letter_is_saved()
    {
        var id = Seed(MatchStage.Evaluated, letter: Letter);

        Assert.True(await EditLetterAsync(id, "  Dobrý den, upraveno.  "));
        Assert.Equal("Dobrý den, upraveno.", Stored(id).CoverLetter);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Edited_letter_cannot_be_empty(string? text)
    {
        var id = Seed(MatchStage.Evaluated, letter: Letter);

        await Assert.ThrowsAsync<ArgumentException>(() => EditLetterAsync(id, text));
        Assert.Equal(Letter, Stored(id).CoverLetter);
    }

    [Fact]
    public async Task Too_long_letter_is_rejected()
    {
        var id = Seed(MatchStage.Evaluated, letter: Letter);

        await Assert.ThrowsAsync<ArgumentException>(() => EditLetterAsync(id, new string('x', UpdateCoverLetter.MaxLength + 1)));
    }

    [Fact]
    public async Task Somebody_elses_letter_cannot_be_edited()
    {
        var id = Seed(MatchStage.Evaluated, letter: Letter, userId: _otherUserId);

        Assert.False(await EditLetterAsync(id, "Cizí"));
        Assert.Equal(Letter, Stored(id).CoverLetter);
    }
}
