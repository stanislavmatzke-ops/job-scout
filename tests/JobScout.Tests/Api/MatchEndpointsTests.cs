using System.Net;
using System.Net.Http.Json;
using JobScout.Core.Domain;
using JobScout.Tests.TestUtils;

namespace JobScout.Tests.Api;

/// <summary>
/// /api/matches – the user's jobs over HTTP: list by tab, detail, status change, notes.
/// Always only the signed-in user's jobs; somebody else's job looks exactly like a missing one (404).
/// Enums travel as text ("Applied"), so the Angular code reads like the domain.
/// </summary>
public sealed class MatchEndpointsTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 10, 6, 0, 0, TimeSpan.Zero);

    private readonly JobScoutApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    // What the Angular app will read – enums as strings
    private sealed record Counts(int Open, int InProgress, int Closed, int TriagedOut);
    private sealed record Item(Guid Id, string Title, string Stage, string Status, int? Score, int? TriageScore,
        string? Summary, bool HasCoverLetter, string Url);
    private sealed record ListResponse(string View, List<Item> Items, Counts Counts);
    private sealed record Evaluation(int Score, string Summary, List<string> Reasons, string RemoteWork);
    private sealed record History(string From, string To, string? Note);
    private sealed record Detail(Guid Id, string Title, string Status, int? Score, Evaluation? Evaluation,
        string? CoverLetter, string? Notes, List<History> History);
    private sealed record Me(Guid Id);

    /// <summary>Signed-in browser + the user's id.</summary>
    private async Task<(HttpClient Browser, Guid UserId)> SignInAsync(string persona = "private")
    {
        var browser = _factory.CreateBrowser();
        await browser.PostAsync($"/api/auth/dev/{persona}", null);
        var me = await browser.GetFromJsonAsync<Me>("/api/me");
        return (browser, me!.Id);
    }

    private Guid Seed(Guid userId, string title, MatchStage stage = MatchStage.Evaluated, int score = 7,
        MatchStatus status = MatchStatus.New, string? letter = null)
    {
        using var db = _factory.CreateDbContext();
        return MatchSeeder.Add(db, userId, title, T0, stage, score, status: status, letter: letter,
            evaluationJson: MatchSeeder.EvaluationJson(score, $"Shrnutí {title}"));
    }

    // ---------- list ----------

    [Fact]
    public async Task List_needs_sign_in()
    {
        var response = await _factory.CreateBrowser().GetAsync("/api/matches");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_shows_my_open_jobs_best_first_with_counts()
    {
        var (browser, me) = await SignInAsync();
        var (_, other) = await SignInAsync("zednik");
        Seed(me, "Architekt", score: 8, letter: "Dobrý den, …");
        Seed(me, "Tech Lead", score: 9);
        Seed(me, "Applied", status: MatchStatus.Applied);
        Seed(other, "Stavbyvedoucí", score: 10);

        var list = await browser.GetFromJsonAsync<ListResponse>("/api/matches");

        Assert.NotNull(list);
        Assert.Equal("Open", list.View);
        Assert.Equal(new[] { "Tech Lead", "Architekt" }, list.Items.Select(i => i.Title).ToArray());
        Assert.Equal(new Counts(Open: 2, InProgress: 1, Closed: 0, TriagedOut: 0), list.Counts);

        var architekt = list.Items[1];
        Assert.Equal(("Evaluated", "New", (int?)8, "Shrnutí Architekt", true),
            (architekt.Stage, architekt.Status, architekt.Score, architekt.Summary, architekt.HasCoverLetter));
    }

    [Theory]
    [InlineData("inprogress")]
    [InlineData("InProgress")]
    public async Task View_is_chosen_by_name_case_insensitive(string view)
    {
        var (browser, me) = await SignInAsync();
        Seed(me, "Applied", status: MatchStatus.Applied);
        Seed(me, "Open");

        var list = await browser.GetFromJsonAsync<ListResponse>($"/api/matches?view={view}");

        Assert.Equal("InProgress", list!.View);
        Assert.Equal("Applied", Assert.Single(list.Items).Title);
    }

    [Theory]
    [InlineData("bogus")]
    [InlineData("5")]          // Enum.TryParse would happily accept a number
    public async Task Unknown_view_is_400(string view)
    {
        var (browser, _) = await SignInAsync();

        var response = await browser.GetAsync($"/api/matches?view={view}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- detail ----------

    [Fact]
    public async Task Detail_of_my_job()
    {
        var (browser, me) = await SignInAsync();
        var id = Seed(me, "Architekt", score: 8, letter: "Dobrý den, …");

        var detail = await browser.GetFromJsonAsync<Detail>($"/api/matches/{id}");

        Assert.NotNull(detail);
        Assert.Equal(("Architekt", "New", (int?)8), (detail.Title, detail.Status, detail.Score));
        Assert.Equal("Shrnutí Architekt", detail.Evaluation!.Summary);
        Assert.Equal("Hybrid", detail.Evaluation.RemoteWork);
        Assert.Equal("Dobrý den, …", detail.CoverLetter);
        Assert.Empty(detail.History);
    }

    [Fact]
    public async Task Somebody_elses_job_is_404_like_a_missing_one()
    {
        var (browser, _) = await SignInAsync();
        var (_, other) = await SignInAsync("zednik");
        var foreign = Seed(other, "Stavbyvedoucí");

        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync($"/api/matches/{foreign}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync($"/api/matches/{Guid.NewGuid()}")).StatusCode);
    }

    // ---------- status ----------

    [Fact]
    public async Task Status_change_moves_the_job_to_another_tab_and_into_history()
    {
        var (browser, me) = await SignInAsync();
        var id = Seed(me, "Architekt");

        var response = await browser.PostAsJsonAsync($"/api/matches/{id}/status",
            new { status = "Applied", note = "Odesláno přes jobs.cz" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var detail = await browser.GetFromJsonAsync<Detail>($"/api/matches/{id}");
        Assert.Equal("Applied", detail!.Status);
        Assert.Equal(new History("New", "Applied", "Odesláno přes jobs.cz"), Assert.Single(detail.History));

        var open = await browser.GetFromJsonAsync<ListResponse>("/api/matches?view=open");
        var inProgress = await browser.GetFromJsonAsync<ListResponse>("/api/matches?view=inprogress");
        Assert.Empty(open!.Items);
        Assert.Equal(id, Assert.Single(inProgress!.Items).Id);
    }

    [Theory]
    [InlineData("""{ "status": "Bogus" }""")]
    [InlineData("""{ "status": 99 }""")]
    [InlineData("""{ "note": "no status" }""")]
    public async Task Invalid_status_is_400(string json)
    {
        var (browser, me) = await SignInAsync();
        var id = Seed(me, "Architekt");

        var response = await browser.PostAsync($"/api/matches/{id}/status",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Too_long_note_is_400()
    {
        var (browser, me) = await SignInAsync();
        var id = Seed(me, "Architekt");

        var response = await browser.PostAsJsonAsync($"/api/matches/{id}/status",
            new { status = "Applied", note = new string('x', 2001) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Cannot_change_somebody_elses_job()
    {
        var (browser, _) = await SignInAsync();
        var (_, other) = await SignInAsync("zednik");
        var foreign = Seed(other, "Stavbyvedoucí");

        var response = await browser.PostAsJsonAsync($"/api/matches/{foreign}/status", new { status = "Hidden" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- notes ----------

    [Fact]
    public async Task Notes_are_saved()
    {
        var (browser, me) = await SignInAsync();
        var id = Seed(me, "Architekt");

        var response = await browser.PutAsJsonAsync($"/api/matches/{id}/notes", new { notes = "Zeptat se na HO" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var detail = await browser.GetFromJsonAsync<Detail>($"/api/matches/{id}");
        Assert.Equal("Zeptat se na HO", detail!.Notes);
    }
}
