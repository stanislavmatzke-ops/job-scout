using JobScout.Application.Abstractions;
using JobScout.Application.Evaluation;
using JobScout.Application.Letters;
using JobScout.Application.Limits;
using JobScout.Application.Llm;
using JobScout.Application.Matches;
using JobScout.Application.Notifications;
using JobScout.Application.Profiles;
using JobScout.Core.Domain;
using JobScout.Core.Jobs;
using JobScout.Tests.TestUtils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobScout.Tests.Application.Limits;

/// <summary>
/// ADR-0021: what a user starts by clicking is limited within any 24 hours – counted from the cost records (LlmCall).
/// The morning run is not counted, admins have no limits, a reached limit stops the use case before the AI is called.
/// </summary>
public sealed class AiQuotaTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Cv = "# Jana Ukázková\nSenior .NET vývojářka, Brno"u8.ToArray();

    private readonly SqliteTestDatabase _database = new();
    private readonly FixedTimeProvider _time = new(Now);
    private readonly AiQuotaGate _gate = new();
    private readonly FakeEmailSender _mail = new();
    private readonly FakeJobSource _jobsCz = new("jobs.cz");
    private readonly FakeLlmClient _llm;
    private readonly Guid _jana;
    private readonly Guid _petr;
    private readonly Guid _boss;
    private AiLimitOptions _options = new();

    public AiQuotaTests()
    {
        _llm = new FakeLlmClient(request => request.Purpose switch
        {
            LlmPurpose.Profile => new LlmResponse(LlmJson.Serialize(TestProfiles.With(["Softwarový architekt"])), "fake-model", "end_turn", 100, 50),
            LlmPurpose.CoverLetter => new LlmResponse("Dobrý den,\n\nreaguji…\n\nS pozdravem", "fake-model", "end_turn", 100, 50),
            _ => new LlmResponse(MatchSeeder.EvaluationJson(6), "fake-model", "end_turn", 100, 50),
        });

        using var db = _database.CreateContext();
        var jana = new User("google", "g-jana", "jana@example.com", "Jana", Now.AddDays(-10));
        var petr = new User("google", "g-petr", "petr@example.com", "Petr", Now.AddDays(-10));
        var boss = new User("microsoft", "ms-boss", "boss@example.com", "Boss", Now.AddDays(-10));
        foreach (var u in new[] { jana, petr, boss })
            u.Approve(Now.AddDays(-10));
        db.AddRange(jana, petr, boss);
        db.SaveChanges();
        (_jana, _petr, _boss) = (jana.Id, petr.Id, boss.Id);
    }

    public void Dispose() => _database.Dispose();

    // ---------- helpers ----------

    private sealed class Admins : IAdminDirectory
    {
        public bool IsAdmin(string provider, string subject) => $"{provider}:{subject}" == "microsoft:ms-boss";
    }

    private AiQuota Quota(JobScout.Infrastructure.Persistence.JobScoutDbContext db) => new(db, _options, _gate, _time,
        NullLogger<AiQuota>.Instance, new Admins(),
        new Notifier(_mail, new NotificationOptions { AdminAddresses = ["admin@example.com"] }, NullLogger<Notifier>.Instance));

    private async Task<AiLimitDenial?> CheckAsync(Guid userId, AiAction action)
    {
        await using var db = _database.CreateContext();
        return await Quota(db).CheckAsync(userId, action);
    }

    /// <summary>A recorded AI call, as the usage recorder (ADR-0016) would have stored it.</summary>
    private void Call(Guid userId, LlmPurpose purpose, TimeSpan ago, long microUsd = 10_000, Guid? scoutRunId = null, bool failed = false)
    {
        using var db = _database.CreateContext();
        db.LlmCalls.Add(failed
            ? LlmCall.Failure(userId, scoutRunId, purpose.ToString(), "fake-model", "HttpRequestException: down", 100, Now - ago)
            : LlmCall.Success(userId, scoutRunId, purpose.ToString(), "fake-model", 100, 50, microUsd, 100, Now - ago));
        db.SaveChanges();
    }

    // ---------- counting ----------

    [Fact]
    public async Task Below_the_limit_a_click_is_allowed()
    {
        Call(_jana, LlmPurpose.Profile, TimeSpan.FromHours(2));
        Call(_jana, LlmPurpose.Profile, TimeSpan.FromHours(1));

        Assert.Null(await CheckAsync(_jana, AiAction.Profile));
    }

    [Fact]
    public async Task The_limit_is_reached_and_says_when_it_frees_up()
    {
        Call(_jana, LlmPurpose.Profile, TimeSpan.FromHours(5));
        Call(_jana, LlmPurpose.Profile, TimeSpan.FromHours(3));
        Call(_jana, LlmPurpose.Profile, TimeSpan.FromHours(1));

        var denial = await CheckAsync(_jana, AiAction.Profile);

        Assert.Equal(new AiLimitDenial(AiLimitReason.Profile, 3, Now.AddHours(-5).AddHours(24)), denial);
    }

    [Fact]
    public async Task The_window_slides_no_waiting_for_midnight()
    {
        Call(_jana, LlmPurpose.Profile, TimeSpan.FromHours(23));
        Call(_jana, LlmPurpose.Profile, TimeSpan.FromHours(3));
        Call(_jana, LlmPurpose.Profile, TimeSpan.FromHours(1));
        Assert.NotNull(await CheckAsync(_jana, AiAction.Profile));

        _time.Now = Now.AddHours(1).AddMinutes(1);                  // the oldest one has left the 24 hours

        Assert.Null(await CheckAsync(_jana, AiAction.Profile));
    }

    [Fact]
    public async Task Each_kind_of_click_has_its_own_limit()
    {
        for (var i = 0; i < 10; i++)
            Call(_jana, LlmPurpose.CoverLetter, TimeSpan.FromMinutes(10 + i));

        Assert.Equal(AiLimitReason.CoverLetter, (await CheckAsync(_jana, AiAction.CoverLetter))?.Reason);
        Assert.Null(await CheckAsync(_jana, AiAction.Evaluation));
        Assert.Null(await CheckAsync(_jana, AiAction.Profile));
    }

    [Fact]
    public async Task The_morning_run_failed_calls_and_other_users_are_not_counted()
    {
        var run = Guid.NewGuid();
        for (var i = 0; i < 5; i++)
        {
            Call(_jana, LlmPurpose.Evaluation, TimeSpan.FromHours(2), scoutRunId: run);     // the morning run has its own limits
            Call(_jana, LlmPurpose.Profile, TimeSpan.FromHours(2), failed: true);          // no answer, nothing to count
            Call(_petr, LlmPurpose.Profile, TimeSpan.FromHours(2));
        }

        Assert.Null(await CheckAsync(_jana, AiAction.Profile));
        Assert.Null(await CheckAsync(_jana, AiAction.Evaluation));
    }

    // ---------- money ----------

    [Fact]
    public async Task A_user_who_spent_the_daily_money_gets_nothing_more()
    {
        Call(_jana, LlmPurpose.Evaluation, TimeSpan.FromHours(4), microUsd: 300_000);
        Call(_jana, LlmPurpose.CoverLetter, TimeSpan.FromHours(2), microUsd: 200_000);       // 0.50 USD together

        var denial = await CheckAsync(_jana, AiAction.Profile);

        Assert.Equal(new AiLimitDenial(AiLimitReason.UserCost, null, Now.AddHours(-4).AddHours(24)), denial);
    }

    [Fact]
    public async Task The_emergency_brake_stops_everybody_and_tells_the_admins_once()
    {
        _options = new AiLimitOptions { AppCostPer24hUsd = 1.00m };
        Call(_petr, LlmPurpose.Evaluation, TimeSpan.FromHours(3), microUsd: 450_000);
        Call(_jana, LlmPurpose.Evaluation, TimeSpan.FromHours(2), microUsd: 450_000);
        Call(Guid.NewGuid(), LlmPurpose.CoverLetter, TimeSpan.FromHours(1), microUsd: 100_000);

        Assert.Equal(AiLimitReason.AppPaused, (await CheckAsync(_jana, AiAction.Evaluation))?.Reason);
        Assert.Equal(AiLimitReason.AppPaused, (await CheckAsync(_petr, AiAction.Profile))?.Reason);

        var mail = Assert.Single(_mail.Sent);
        Assert.Equal("admin@example.com", mail.To);
        Assert.Contains("pozastavené", mail.Subject);
    }

    [Fact]
    public async Task Admins_have_no_limits_and_their_spending_does_not_stop_the_others()
    {
        _options = new AiLimitOptions { AppCostPer24hUsd = 1.00m };
        for (var i = 0; i < 25; i++)
            Call(_boss, LlmPurpose.Evaluation, TimeSpan.FromMinutes(i + 1), microUsd: 100_000);    // 2.50 USD

        Assert.Null(await CheckAsync(_boss, AiAction.Evaluation));
        Assert.Null(await CheckAsync(_jana, AiAction.Evaluation));
        Assert.Empty(_mail.Sent);
    }

    [Fact]
    public async Task Limits_can_be_switched_off()
    {
        _options = new AiLimitOptions { Enabled = false };
        for (var i = 0; i < 5; i++)
            Call(_jana, LlmPurpose.Profile, TimeSpan.FromHours(1));

        Assert.Null(await CheckAsync(_jana, AiAction.Profile));
    }

    // ---------- what the UI shows ----------

    [Fact]
    public async Task Status_tells_what_is_left()
    {
        Call(_jana, LlmPurpose.Profile, TimeSpan.FromHours(6));
        Call(_jana, LlmPurpose.CoverLetter, TimeSpan.FromHours(2), microUsd: 40_000);

        await using var db = _database.CreateContext();
        var status = await Quota(db).GetStatusAsync(_jana);

        Assert.False(status.Unlimited);
        Assert.Equal((1, 3, 2), (status.Profile.Used, status.Profile.Limit, status.Profile.Left));
        Assert.Equal(Now.AddHours(-6).AddHours(24), status.Profile.ResetsAt);
        Assert.Equal((1, 10), (status.CoverLetters.Used, status.CoverLetters.Limit));
        Assert.Equal((0, 20, (DateTimeOffset?)null), (status.Evaluations.Used, status.Evaluations.Limit, status.Evaluations.ResetsAt));
        Assert.Equal(2, status.LetterRewritesPerJob);
        Assert.Equal((0.05m, 0.50m), (status.CostUsd, status.CostLimitUsd));
        Assert.False(status.Paused);
    }

    [Fact]
    public async Task Status_of_an_admin_is_unlimited()
    {
        await using var db = _database.CreateContext();

        Assert.True((await Quota(db).GetStatusAsync(_boss)).Unlimited);
    }

    // ---------- one click at a time ----------

    [Fact]
    public async Task The_next_click_of_the_same_user_waits_for_the_previous_one()
    {
        await using var db1 = _database.CreateContext();
        await using var db2 = _database.CreateContext();
        await using var db3 = _database.CreateContext();

        var first = await Quota(db1).EnterAsync(_jana, AiAction.Evaluation);
        var second = Quota(db2).EnterAsync(_jana, AiAction.Evaluation);
        var otherUser = await Quota(db3).EnterAsync(_petr, AiAction.Evaluation);   // others don't wait

        await Task.Delay(50);
        Assert.False(second.IsCompleted);

        first.Dispose();
        (await second).Dispose();
        otherUser.Dispose();
    }

    [Fact]
    public async Task A_refused_click_does_not_block_the_next_one()
    {
        for (var i = 0; i < 3; i++)
            Call(_jana, LlmPurpose.Profile, TimeSpan.FromHours(1));

        await using (var db = _database.CreateContext())
            await Assert.ThrowsAsync<AiLimitExceededException>(() => Quota(db).EnterAsync(_jana, AiAction.Profile));

        await using (var db = _database.CreateContext())
            (await Quota(db).EnterAsync(_jana, AiAction.Evaluation)).Dispose();     // would hang if the slot stayed taken
    }

    // ---------- in the use cases ----------

    private bool _hasProfile;

    private Guid SeedMatch(MatchStage stage, string? letter = null, int rewrites = 0)
    {
        using var db = _database.CreateContext();
        if (!_hasProfile)
            ProfileSeeder.Add(db, _jana, Now.AddDays(-1));
        _hasProfile = true;
        var id = MatchSeeder.Add(db, _jana, "Solution Architect", Now.AddDays(-1), stage, letter: letter);
        var match = db.JobMatches.Include(m => m.JobOffer).Single(m => m.Id == id);
        for (var i = 0; i < rewrites; i++)
            match.RewriteCoverLetter($"Návrh {i + 2}", Now.AddDays(-1));
        db.SaveChanges();
        var offer = match.JobOffer!;
        _jobsCz.Details[offer.ExternalId] = new JobDetail("jobs.cz", offer.ExternalId, offer.Url, offer.Title, offer.Company,
            "Hledáme architekta.", true);
        return id;
    }

    [Fact]
    public async Task Evaluation_over_the_limit_calls_no_ai_and_changes_nothing()
    {
        var id = SeedMatch(MatchStage.Shortlisted);
        for (var i = 0; i < 20; i++)
            Call(_jana, LlmPurpose.Evaluation, TimeSpan.FromMinutes(i + 1), microUsd: 1_000);

        await using var db = _database.CreateContext();
        var evaluator = new MatchEvaluator([_jobsCz], new JobEvaluator(_llm), _time);
        var ex = await Assert.ThrowsAsync<AiLimitExceededException>(() =>
            new EvaluateMatchNow(db, evaluator, Quota(db)).ExecuteAsync(_jana, id));

        Assert.Equal(AiLimitReason.Evaluation, ex.Denial.Reason);
        Assert.Equal(0, _llm.Calls);
        await using var check = _database.CreateContext();
        Assert.Equal(MatchStage.Shortlisted, check.JobMatches.Single(m => m.Id == id).Stage);
    }

    [Fact]
    public async Task A_letter_can_be_rewritten_twice_per_job_then_only_edited()
    {
        var fresh = SeedMatch(MatchStage.Evaluated, letter: "Ranní návrh", rewrites: 1);
        var used = SeedMatch(MatchStage.Evaluated, letter: "Ranní návrh", rewrites: 2);

        await using (var db = _database.CreateContext())
        {
            var result = await new WriteCoverLetterNow(db, new CoverLetterWriter(_llm), _time, Quota(db)).ExecuteAsync(_jana, fresh, replace: true);
            Assert.Equal(WriteLetterStatus.Written, result.Status);
        }
        await using (var db = _database.CreateContext())
        {
            var ex = await Assert.ThrowsAsync<AiLimitExceededException>(() =>
                new WriteCoverLetterNow(db, new CoverLetterWriter(_llm), _time, Quota(db)).ExecuteAsync(_jana, used, replace: true));
            Assert.Equal(new AiLimitDenial(AiLimitReason.LetterRewrites, 2, null), ex.Denial);
        }

        await using var check = _database.CreateContext();
        Assert.Equal(2, check.JobMatches.Single(m => m.Id == fresh).LetterRewrites);
        Assert.Equal(1, _llm.Calls);                                    // only the allowed one
    }

    [Fact]
    public async Task The_first_letter_of_a_job_is_not_a_rewrite()
    {
        var id = SeedMatch(MatchStage.Evaluated);

        await using var db = _database.CreateContext();
        await new WriteCoverLetterNow(db, new CoverLetterWriter(_llm), _time, Quota(db)).ExecuteAsync(_jana, id);

        await using var check = _database.CreateContext();
        var match = check.JobMatches.Single(m => m.Id == id);
        Assert.Equal((true, 0), (match.CoverLetter is not null, match.LetterRewrites));
    }

    [Fact]
    public async Task Over_the_profile_limit_the_same_cv_still_works_a_new_one_is_refused_and_not_stored()
    {
        await using (var db = _database.CreateContext())
            await new UploadCv(db, new AnalyzeProfile(db, new ProfileExtractor(_llm), _time), Quota(db)).ExecuteAsync(_jana, "cv.md", Cv);
        for (var i = 0; i < 3; i++)
            Call(_jana, LlmPurpose.Profile, TimeSpan.FromMinutes(i + 1));
        var callsBefore = _llm.Calls;

        // The same CV again: no AI → not counted, not refused
        await using (var db = _database.CreateContext())
            await new UploadCv(db, new AnalyzeProfile(db, new ProfileExtractor(_llm), _time), Quota(db)).ExecuteAsync(_jana, "cv.md", Cv);

        // A different CV: refused before it is stored
        await using (var db = _database.CreateContext())
            await Assert.ThrowsAsync<AiLimitExceededException>(() =>
                new UploadCv(db, new AnalyzeProfile(db, new ProfileExtractor(_llm), _time), Quota(db))
                    .ExecuteAsync(_jana, "novy.md", "# Jana\nUplně jiné CV"u8.ToArray()));

        Assert.Equal(callsBefore, _llm.Calls);
        await using var check = _database.CreateContext();
        Assert.Equal("cv.md", check.StoredCvs.Single(c => c.UserId == _jana).FileName);
    }

    [Fact]
    public async Task Changed_notes_over_the_limit_are_refused()
    {
        await using (var db = _database.CreateContext())
            await new UploadCv(db, new AnalyzeProfile(db, new ProfileExtractor(_llm), _time)).ExecuteAsync(_jana, "cv.md", Cv);
        for (var i = 0; i < 3; i++)
            Call(_jana, LlmPurpose.Profile, TimeSpan.FromMinutes(i + 1));

        await using var db2 = _database.CreateContext();
        var ex = await Assert.ThrowsAsync<AiLimitExceededException>(() =>
            new UpdateProfileNotes(db2, new AnalyzeProfile(db2, new ProfileExtractor(_llm), _time), Quota(db2))
                .ExecuteAsync(_jana, "Chci full remote."));

        Assert.Equal(AiLimitReason.Profile, ex.Denial.Reason);
    }
}
