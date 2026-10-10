using JobScout.Application.Matches;
using JobScout.Core.Domain;
using JobScout.Tests.TestUtils;

namespace JobScout.Tests.Application.Matches;

/// <summary>
/// The user's list: every job that passed triage (evaluated or still waiting), best first –
/// AI pre-selects, the user decides. Only the user's own jobs, counts for every tab.
/// </summary>
public sealed class ListMatchesTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 10, 6, 0, 0, TimeSpan.Zero);

    private readonly SqliteTestDatabase _database = new();
    private readonly Guid _userId;
    private readonly Guid _otherUserId;

    public ListMatchesTests()
    {
        using var db = _database.CreateContext();
        var user = new User("dev", "private", "", "private", T0);
        var other = new User("dev", "zednik", "", "zednik", T0);
        db.AddRange(user, other);
        db.SaveChanges();
        (_userId, _otherUserId) = (user.Id, other.Id);
    }

    public void Dispose() => _database.Dispose();

    private Guid Seed(string title, MatchStage stage = MatchStage.Evaluated, int score = 7, int triage = 4,
        MatchStatus status = MatchStatus.New, Guid? userId = null, int minutes = 0, string? letter = null,
        string? evaluationJson = null)
    {
        using var db = _database.CreateContext();
        return MatchSeeder.Add(db, userId ?? _userId, title, T0.AddMinutes(minutes), stage, score, triage, status,
            letter, evaluationJson);
    }

    private async Task<MatchList> ListAsync(MatchView view)
    {
        await using var db = _database.CreateContext();
        return await new ListMatches(db).ExecuteAsync(_userId, view);
    }

    // ---------- open ----------

    [Fact]
    public async Task Open_lists_evaluated_and_waiting_jobs_best_first()
    {
        Seed("Waiting, relevance 2", MatchStage.Shortlisted, triage: 2);
        Seed("Evaluated 6", score: 6);
        Seed("Waiting, relevance 4", MatchStage.Shortlisted, triage: 4);
        Seed("Evaluated 9", score: 9);

        var list = await ListAsync(MatchView.Open);

        // evaluated by score, then the ones without a score by triage relevance
        Assert.Equal(
            new[] { "Evaluated 9", "Evaluated 6", "Waiting, relevance 4", "Waiting, relevance 2" },
            list.Items.Select(i => i.Title).ToArray());
    }

    [Fact]
    public async Task Open_leaves_out_triaged_out_hidden_untriaged_and_acted_on_jobs()
    {
        Seed("Open");
        Seed("Triaged out", MatchStage.TriagedOut);
        Seed("Hidden", status: MatchStatus.Hidden);
        Seed("Found", MatchStage.Found);
        Seed("Applied", status: MatchStatus.Applied);

        var list = await ListAsync(MatchView.Open);

        Assert.Equal("Open", Assert.Single(list.Items).Title);
    }

    [Fact]
    public async Task Only_my_jobs_are_listed()
    {
        Seed("Mine");
        Seed("Somebody else's", userId: _otherUserId);

        var list = await ListAsync(MatchView.Open);

        Assert.Equal("Mine", Assert.Single(list.Items).Title);
        Assert.Equal(1, list.Counts.Open);
    }

    [Fact]
    public async Task Item_carries_summary_letter_flag_and_link()
    {
        Seed("Architekt", score: 8, letter: "Dobrý den, …",
            evaluationJson: MatchSeeder.EvaluationJson(8, "Sedí role i obor."));

        var item = Assert.Single((await ListAsync(MatchView.Open)).Items);

        Assert.Equal(8, item.Score);
        Assert.Equal("Sedí role i obor.", item.Summary);
        Assert.True(item.HasCoverLetter);
        Assert.StartsWith("https://www.jobs.cz/rpd/", item.Url);
        Assert.Equal(MatchStage.Evaluated, item.Stage);
        Assert.Equal(MatchStatus.New, item.Status);
    }

    [Fact]
    public async Task Broken_evaluation_json_does_not_break_the_list()
    {
        Seed("Old format", evaluationJson: "{ this is not json");

        var item = Assert.Single((await ListAsync(MatchView.Open)).Items);

        Assert.Null(item.Summary);
        Assert.Equal(7, item.Score);
    }

    // ---------- other views ----------

    [Fact]
    public async Task In_progress_lists_what_I_acted_on_most_recent_first()
    {
        Seed("Applied first", status: MatchStatus.Applied, minutes: 1);
        Seed("Second round", status: MatchStatus.Round2, minutes: 2);
        Seed("Open", minutes: 3);

        var list = await ListAsync(MatchView.InProgress);

        Assert.Equal(new[] { "Second round", "Applied first" }, list.Items.Select(i => i.Title).ToArray());
    }

    [Fact]
    public async Task Triaged_out_view_is_the_safety_net_with_the_reason()
    {
        Seed("Statik", MatchStage.TriagedOut);

        var item = Assert.Single((await ListAsync(MatchView.TriagedOut)).Items);

        Assert.Equal("Jiný obor", item.TriageReason);
        Assert.Null(item.Score);
    }

    [Fact]
    public async Task Counts_cover_every_tab_whatever_view_is_asked_for()
    {
        Seed("Open 1");
        Seed("Open 2", MatchStage.Shortlisted);
        Seed("Applied", status: MatchStatus.Applied);
        Seed("Dismissed", status: MatchStatus.Dismissed);
        Seed("Rejected", status: MatchStatus.Rejected);
        Seed("Triaged out", MatchStage.TriagedOut);
        Seed("Hidden", status: MatchStatus.Hidden);

        var list = await ListAsync(MatchView.Closed);

        Assert.Equal(new MatchCounts(Open: 2, InProgress: 1, Closed: 2, TriagedOut: 1), list.Counts);
        Assert.Equal(MatchView.Closed, list.View);
        Assert.Equal(2, list.Items.Count);
    }
}
