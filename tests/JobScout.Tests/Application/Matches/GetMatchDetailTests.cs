using JobScout.Application.Matches;
using JobScout.Core.Domain;
using JobScout.Tests.TestUtils;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Tests.Application.Matches;

/// <summary>Everything about one job of mine: the ad, AI evaluation, letter, notes and history. Not mine = not found.</summary>
public sealed class GetMatchDetailTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 10, 6, 0, 0, TimeSpan.Zero);

    private readonly SqliteTestDatabase _database = new();
    private readonly Guid _userId;
    private readonly Guid _otherUserId;

    public GetMatchDetailTests()
    {
        using var db = _database.CreateContext();
        var user = new User("dev", "private", "", "private", T0);
        var other = new User("dev", "zednik", "", "zednik", T0);
        db.AddRange(user, other);
        db.SaveChanges();
        (_userId, _otherUserId) = (user.Id, other.Id);
    }

    public void Dispose() => _database.Dispose();

    private async Task<MatchDetail?> GetAsync(Guid matchId, Guid? asUser = null)
    {
        await using var db = _database.CreateContext();
        return await new GetMatchDetail(db).ExecuteAsync(asUser ?? _userId, matchId);
    }

    [Fact]
    public async Task Detail_has_the_ad_evaluation_letter_and_notes()
    {
        Guid id;
        await using (var db = _database.CreateContext())
            id = MatchSeeder.Add(db, _userId, "Solution Architect", T0, score: 8, letter: "Dobrý den, …",
                evaluationJson: MatchSeeder.EvaluationJson(8, "Sedí role."), adText: "Hledáme architekta…",
                adTextComplete: false, company: "CloudNest s.r.o.");

        var detail = await GetAsync(id);

        Assert.NotNull(detail);
        Assert.Equal(("Solution Architect", "CloudNest s.r.o.", "jobs.cz"), (detail.Title, detail.Company, detail.Source));
        Assert.Equal(8, detail.Score);
        Assert.NotNull(detail.Evaluation);
        Assert.Equal("Sedí role.", detail.Evaluation.Summary);
        Assert.Equal(new[] { "Sedí role" }, detail.Evaluation.Reasons);
        Assert.Equal("Dobrý den, …", detail.CoverLetter);
        Assert.Equal("Hledáme architekta…", detail.AdText);
        Assert.True(detail.AdTextIncomplete);
    }

    [Fact]
    public async Task History_is_in_order_with_notes()
    {
        Guid id;
        await using (var db = _database.CreateContext())
            id = MatchSeeder.Add(db, _userId, "Tech Lead", T0);

        await using (var db = _database.CreateContext())
        {
            var match = await db.JobMatches.SingleAsync(m => m.Id == id);
            match.ChangeStatus(MatchStatus.Applied, "Odesláno přes jobs.cz", T0.AddDays(1));
            match.ChangeStatus(MatchStatus.Call, "Volala HR, pohovor v pátek", T0.AddDays(2));
            match.UpdateNotes("Zeptat se na HO", T0.AddDays(2));
            await db.SaveChangesAsync();
        }

        var detail = await GetAsync(id);

        Assert.NotNull(detail);
        Assert.Equal(MatchStatus.Call, detail.Status);
        Assert.Equal("Zeptat se na HO", detail.Notes);
        Assert.Equal(
            new[] { (MatchStatus.New, MatchStatus.Applied, "Odesláno přes jobs.cz"), (MatchStatus.Applied, MatchStatus.Call, "Volala HR, pohovor v pátek") },
            detail.History.Select(h => (h.From, h.To, h.Note!)).ToArray());
    }

    [Fact]
    public async Task Waiting_job_has_no_evaluation_yet()
    {
        Guid id;
        await using (var db = _database.CreateContext())
            id = MatchSeeder.Add(db, _userId, "Waiting", T0, MatchStage.Shortlisted, triage: 2);

        var detail = await GetAsync(id);

        Assert.NotNull(detail);
        Assert.Null(detail.Evaluation);
        Assert.Null(detail.Score);
        Assert.Equal(2, detail.TriageScore);
        Assert.Null(detail.AdText);
    }

    [Fact]
    public async Task Somebody_elses_job_is_not_found()
    {
        Guid id;
        await using (var db = _database.CreateContext())
            id = MatchSeeder.Add(db, _otherUserId, "Not mine", T0);

        Assert.Null(await GetAsync(id));
    }

    [Fact]
    public async Task Unknown_id_is_not_found()
    {
        Assert.Null(await GetAsync(Guid.NewGuid()));
    }
}
