using JobScout.Application.Reports;
using JobScout.Core.Domain;
using JobScout.Core.Jobs;
using JobScout.Tests.TestUtils;

namespace JobScout.Tests.Application.Reports;

/// <summary>
/// Morning report data: what is new today (recommended 7+ with a letter first), what was recommended earlier
/// and is still open, and what waits for the user's decision (relevance 2, not evaluated automatically).
/// </summary>
public sealed class BuildMorningReportTests : IDisposable
{
    private static readonly DateTimeOffset Yesterday = new(2026, 10, 8, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RunStart = new(2026, 10, 9, 6, 0, 0, TimeSpan.Zero);

    private const string Evaluation = """
        {"score":8,"summary":"Sedí role i technologie.","reasons":["Azure","Banking"],"risks":["Mzda neuvedena"],
         "questions":["Kolik dní HO?"],"salary":"","remoteWork":"hybrid","employmentType":"employee"}
        """;

    private readonly SqliteTestDatabase _database = new();
    private readonly FixedTimeProvider _time = new(RunStart.AddMinutes(5));
    private readonly Guid _userId;
    private readonly Guid _otherUserId;
    private int _nextId;

    private static readonly ReportStats Stats = new(Found: 157, NewForUser: 50, Evaluated: 15, Letters: 5, CostUsd: 0.36m);

    public BuildMorningReportTests()
    {
        using var db = _database.CreateContext();
        var user = new User("dev", "private", "", "private", Yesterday);
        var other = new User("dev", "zednik", "", "zednik", Yesterday);
        db.AddRange(user, other);
        db.SaveChanges();
        (_userId, _otherUserId) = (user.Id, other.Id);
    }

    public void Dispose() => _database.Dispose();

    /// <summary>Evaluated job; "when" = time of the last change (evaluation / letter).</summary>
    private void Evaluated(string title, int score, DateTimeOffset when, string? letter = null,
        MatchStatus status = MatchStatus.New, Guid? userId = null, string? evaluationJson = null, bool complete = true)
    {
        using var db = _database.CreateContext();
        var offer = NewOffer(title, complete);
        var match = new JobMatch(userId ?? _userId, offer.Id, 2, Yesterday);
        match.Shortlist(4, Yesterday);
        match.ApplyEvaluation(score, evaluationJson ?? Evaluation.Replace("\"score\":8", $"\"score\":{score}"), when);
        if (letter is not null) match.SetCoverLetter(letter, when);
        if (status != MatchStatus.New) match.ChangeStatus(status, null, when);
        db.AddRange(offer, match);
        db.SaveChanges();
    }

    private void Waiting(string title, int relevance)
    {
        using var db = _database.CreateContext();
        var offer = NewOffer(title, complete: true);
        var match = new JobMatch(_userId, offer.Id, 2, Yesterday);
        match.Shortlist(relevance, RunStart);
        db.AddRange(offer, match);
        db.SaveChanges();
    }

    private JobOffer NewOffer(string title, bool complete)
    {
        var id = $"2001{++_nextId:D6}";
        var offer = JobOffer.FromListing(new JobListing("jobs.cz", id, title, "Firma s.r.o.", "Praha",
            $"https://www.jobs.cz/rpd/{id}/", []), Yesterday);
        offer.SetDetail(new JobDetail("jobs.cz", id, offer.Url, title, offer.Company, "Text", complete), Yesterday);
        return offer;
    }

    private async Task<MorningReport> BuildAsync()
    {
        await using var db = _database.CreateContext();
        return await new BuildMorningReport(db, _time).ExecuteAsync(_userId, "private", RunStart, Stats);
    }

    [Fact]
    public async Task Todays_recommended_jobs_come_first_best_first_with_their_letters()
    {
        Evaluated("Architekt A", 7, RunStart.AddMinutes(1), letter: "Dopis A");
        Evaluated("Architekt B", 9, RunStart.AddMinutes(2), letter: "Dopis B");

        var report = await BuildAsync();

        Assert.Equal(new[] { "Architekt B", "Architekt A" }, report.NewRecommended.Select(j => j.Title).ToArray());
        Assert.Equal("Dopis B", report.NewRecommended[0].CoverLetter);
    }

    [Fact]
    public async Task Todays_lower_scores_are_listed_separately()
    {
        Evaluated("Vývojář", 5, RunStart.AddMinutes(1));

        var report = await BuildAsync();

        Assert.Empty(report.NewRecommended);
        Assert.Equal("Vývojář", Assert.Single(report.NewOther).Title);
    }

    [Fact]
    public async Task Earlier_recommendations_that_are_still_open_are_kept_below()
    {
        Evaluated("Včerejší architekt", 8, Yesterday, letter: "Dopis");
        Evaluated("Včerejší vývojář", 5, Yesterday);            // earlier and below 7 – not repeated

        var report = await BuildAsync();

        Assert.Equal("Včerejší architekt", Assert.Single(report.EarlierRecommended).Title);
        Assert.Empty(report.NewRecommended);
        Assert.Empty(report.NewOther);
    }

    [Theory]
    [InlineData(MatchStatus.Hidden)]
    [InlineData(MatchStatus.Dismissed)]
    [InlineData(MatchStatus.Applied)]
    public async Task Jobs_the_user_already_handled_are_not_in_the_report(MatchStatus status)
    {
        Evaluated("Architekt", 9, RunStart.AddMinutes(1), letter: "Dopis", status: status);

        var report = await BuildAsync();

        Assert.Empty(report.NewRecommended);
        Assert.Empty(report.EarlierRecommended);
    }

    [Fact]
    public async Task Jobs_waiting_for_the_users_decision_are_listed_with_links()
    {
        Waiting("Analytik", relevance: 2);
        Waiting("Senior vývojář", relevance: 4);                 // waits for the next automatic run – not "for the user"

        var report = await BuildAsync();

        var waiting = Assert.Single(report.WaitingForUser);
        Assert.Equal("Analytik", waiting.Title);
        Assert.StartsWith("https://www.jobs.cz/rpd/", waiting.Url);
        Assert.Equal(2, waiting.Relevance);
    }

    [Fact]
    public async Task Evaluation_details_are_unpacked_for_display()
    {
        Evaluated("Architekt", 8, RunStart.AddMinutes(1), letter: "Dopis", complete: false);

        var job = Assert.Single((await BuildAsync()).NewRecommended);

        Assert.Equal(8, job.Score);
        Assert.Equal("Sedí role i technologie.", job.Summary);
        Assert.Equal(new[] { "Azure", "Banking" }, job.Reasons.ToArray());
        Assert.Equal("Mzda neuvedena", Assert.Single(job.Risks));
        Assert.Equal("Kolik dní HO?", Assert.Single(job.Questions));
        Assert.True(job.DetailIncomplete);
    }

    [Fact]
    public async Task Broken_or_partial_evaluation_json_does_not_break_the_report()
    {
        Evaluated("Starý záznam", 8, RunStart.AddMinutes(1), evaluationJson: "{}");
        Evaluated("Rozbitý záznam", 8, RunStart.AddMinutes(1), evaluationJson: "not json");

        var report = await BuildAsync();

        Assert.Equal(2, report.NewRecommended.Count);
        Assert.All(report.NewRecommended, j => Assert.Empty(j.Reasons));
    }

    [Fact]
    public async Task Other_users_jobs_are_not_included()
    {
        Evaluated("Stavbyvedoucí", 9, RunStart.AddMinutes(1), userId: _otherUserId);

        Assert.Empty((await BuildAsync()).NewRecommended);
    }

    [Fact]
    public async Task Header_carries_subject_time_and_run_stats()
    {
        var report = await BuildAsync();

        Assert.Equal("private", report.Subject);
        Assert.Equal(RunStart.AddMinutes(5), report.GeneratedAt);
        Assert.Equal(Stats, report.Stats);
    }
}
