using JobScout.Application.Evaluation;
using JobScout.Application.Llm;
using JobScout.Application.Matches;
using JobScout.Application.Profiles;
using JobScout.Core.Domain;
using JobScout.Core.Jobs;
using JobScout.Tests.TestUtils;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Tests.Application.Matches;

/// <summary>
/// 6d: after a CV / notes change, old evaluations are not recalculated automatically (cost) – they are marked as
/// evaluated with an older profile version, and only those can be evaluated again.
/// </summary>
public sealed class OutdatedEvaluationTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 10, 6, 0, 0, TimeSpan.Zero);

    private readonly SqliteTestDatabase _database = new();
    private readonly FixedTimeProvider _time = new(T0.AddDays(1));
    private readonly FakeJobSource _jobsCz = new("jobs.cz");
    private readonly FakeLlmClient _llm = new(MatchSeeder.EvaluationJson(6, "Podle nového profilu."));
    private readonly Guid _userId;

    public OutdatedEvaluationTests()
    {
        using var db = _database.CreateContext();
        var user = new User("dev", "private", "", "private", T0);
        db.Users.Add(user);
        db.SaveChanges();
        _userId = user.Id;
        ProfileSeeder.Add(db, _userId, T0, version: 1);
    }

    public void Dispose() => _database.Dispose();

    /// <summary>The user changed the CV – profile v2 is now the current one.</summary>
    private void NewProfileVersion()
    {
        using var db = _database.CreateContext();
        ProfileSeeder.Add(db, _userId, T0.AddHours(12), version: 2, "Tech Lead");
    }

    private Guid Seed(string title, MatchStage stage = MatchStage.Evaluated, int profileVersion = 1,
        MatchStatus status = MatchStatus.New)
    {
        using var db = _database.CreateContext();
        return MatchSeeder.Add(db, _userId, title, T0, stage, score: 8, status: status, adText: "Hledáme architekta.",
            profileVersion: profileVersion);
    }

    private async Task<MatchDetail> DetailAsync(Guid id)
    {
        await using var db = _database.CreateContext();
        return (await new GetMatchDetail(db).ExecuteAsync(_userId, id))!;
    }

    // ---------- marking ----------

    [Fact]
    public async Task Evaluation_with_the_current_profile_is_not_outdated()
    {
        var id = Seed("Architekt");

        Assert.False((await DetailAsync(id)).EvaluationOutdated);
    }

    [Fact]
    public async Task Evaluation_with_an_older_profile_is_outdated_in_detail_and_list()
    {
        var id = Seed("Architekt");
        NewProfileVersion();

        Assert.True((await DetailAsync(id)).EvaluationOutdated);

        await using var db = _database.CreateContext();
        var item = Assert.Single((await new ListMatches(db).ExecuteAsync(_userId, MatchView.Open)).Items);
        Assert.True(item.EvaluationOutdated);
    }

    [Fact]
    public async Task Job_without_an_evaluation_is_never_outdated()
    {
        var id = Seed("Čeká", MatchStage.Shortlisted);
        NewProfileVersion();

        Assert.False((await DetailAsync(id)).EvaluationOutdated);
    }

    // ---------- re-evaluating records the version ----------

    [Fact]
    public async Task Evaluating_again_records_the_current_profile_version()
    {
        var id = Seed("Architekt");
        NewProfileVersion();

        await using (var db = _database.CreateContext())
        {
            var evaluator = new MatchEvaluator([_jobsCz], new JobEvaluator(_llm), _time);
            Assert.Equal(EvaluateNowStatus.Evaluated, (await new EvaluateMatchNow(db, evaluator).ExecuteAsync(_userId, id)).Status);
        }

        var detail = await DetailAsync(id);
        Assert.False(detail.EvaluationOutdated);
        Assert.Equal("Podle nového profilu.", detail.Evaluation!.Summary);
        Assert.Contains("Tech Lead", _llm.Requests.Single().UserPrompt);          // the NEW profile was used
    }

    [Fact]
    public async Task Morning_run_records_the_profile_version_it_evaluated_with()
    {
        Guid id;
        await using (var db = _database.CreateContext())
            id = MatchSeeder.Add(db, _userId, "Architekt", T0, MatchStage.Shortlisted, triage: 4, profileVersion: 1);
        await using (var db = _database.CreateContext())
        {
            var offer = db.JobMatches.Include(m => m.JobOffer).Single(m => m.Id == id).JobOffer!;
            _jobsCz.Details[offer.ExternalId] = new JobDetail("jobs.cz", offer.ExternalId, offer.Url, offer.Title, offer.Company, "Text", true);
        }
        NewProfileVersion();

        await using (var db = _database.CreateContext())
            await new EvaluateJobs(db, new MatchEvaluator([_jobsCz], new JobEvaluator(_llm), _time))
                .ExecuteAsync(_userId, TestProfiles.With(["Tech Lead"]), profileVersion: 2);

        await using var check = _database.CreateContext();
        Assert.Equal(2, (await check.JobMatches.SingleAsync(m => m.Id == id)).ProfileVersion);
    }

    // ---------- the profile page ----------

    [Fact]
    public async Task Profile_counts_outdated_evaluations_waiting_for_a_decision()
    {
        Seed("Outdated, open 1");
        Seed("Outdated, open 2");
        Seed("Outdated, but already applied", status: MatchStatus.Applied);   // decided – not worth re-evaluating
        Seed("Outdated, triaged out", MatchStage.TriagedOut);
        NewProfileVersion();
        Seed("Fresh", profileVersion: 2);

        await using var db = _database.CreateContext();
        var view = await new GetProfile(db).ExecuteAsync(_userId);

        Assert.Equal(2, view.OutdatedEvaluations);
    }

    [Fact]
    public async Task Current_profile_comes_with_its_version()
    {
        NewProfileVersion();

        await using var db = _database.CreateContext();
        var current = await db.CurrentProfileAsync(_userId);

        Assert.NotNull(current);
        Assert.Equal(2, current.Version);
        Assert.Contains("Tech Lead", current.Profile.TargetRoles);
    }
}
