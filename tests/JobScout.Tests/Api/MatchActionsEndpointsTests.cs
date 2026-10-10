using System.Net;
using System.Net.Http.Json;
using JobScout.Application.Llm;
using JobScout.Core.Domain;
using JobScout.Core.Jobs;
using JobScout.Tests.TestUtils;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Tests.Api;

/// <summary>
/// The buttons of the UI: "Ohodnotit", "Napsat dopis", editing the letter. Each answer tells the UI what to show:
/// 409 = can't do it in this state, 410 = the ad is gone, 502/503 = AI or portal failed, try again.
/// </summary>
public sealed class MatchActionsEndpointsTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 10, 6, 0, 0, TimeSpan.Zero);
    private const string Letter = "Dobrý den,\n\nreaguji na Vaši nabídku…\n\nS pozdravem\n[Jméno a příjmení]";

    private readonly JobScoutApiFactory _factory = new();

    public MatchActionsEndpointsTests()
    {
        _factory.AiAnswers[LlmPurpose.Evaluation] = (MatchSeeder.EvaluationJson(6, "Částečná shoda."), "end_turn");
        _factory.AiAnswers[LlmPurpose.CoverLetter] = (Letter, "end_turn");
    }

    public void Dispose() => _factory.Dispose();

    private sealed record Evaluation(int Score, string Summary);
    private sealed record Detail(Guid Id, string Stage, int? Score, Evaluation? Evaluation, string? CoverLetter);
    private sealed record Me(Guid Id);

    private async Task<(HttpClient Browser, Guid UserId)> SignInAsync(string persona = "private", bool withProfile = true)
    {
        var browser = _factory.CreateBrowser();
        await browser.PostAsync($"/api/auth/dev/{persona}", null);
        var me = (await browser.GetFromJsonAsync<Me>("/api/me"))!.Id;
        if (withProfile)
        {
            await using var db = _factory.CreateDbContext();
            ProfileSeeder.Add(db, me, T0);
        }
        return (browser, me);
    }

    private Guid Seed(Guid userId, MatchStage stage, string? letter = null, bool portalKnowsIt = true)
    {
        using var db = _factory.CreateDbContext();
        var id = MatchSeeder.Add(db, userId, "Solution Architect", T0, stage, score: 8, triage: 2, letter: letter,
            adText: stage == MatchStage.Evaluated ? "Hledáme architekta." : null);
        var offer = db.JobMatches.Include(m => m.JobOffer).Single(m => m.Id == id).JobOffer!;
        if (portalKnowsIt)
            _factory.JobsCz.Details[offer.ExternalId] = new JobDetail("jobs.cz", offer.ExternalId, offer.Url, offer.Title,
                offer.Company, "Hledáme architekta, hybrid Praha.", true);
        return id;
    }

    // ---------- evaluate ----------

    [Fact]
    public async Task Evaluate_needs_sign_in()
    {
        var response = await _factory.CreateBrowser().PostAsync($"/api/matches/{Guid.NewGuid()}/evaluate", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Evaluate_returns_the_updated_detail()
    {
        var (browser, me) = await SignInAsync();
        var id = Seed(me, MatchStage.Shortlisted);

        var response = await browser.PostAsync($"/api/matches/{id}/evaluate", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<Detail>();
        Assert.Equal(("Evaluated", (int?)6, "Částečná shoda."), (detail!.Stage, detail.Score, detail.Evaluation!.Summary));
    }

    [Fact]
    public async Task Evaluate_without_profile_is_409()
    {
        var (browser, me) = await SignInAsync(withProfile: false);
        var id = Seed(me, MatchStage.Shortlisted);

        var response = await browser.PostAsync($"/api/matches/{id}/evaluate", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, _factory.Llm.Calls);
    }

    [Fact]
    public async Task Evaluate_removed_ad_is_410()
    {
        var (browser, me) = await SignInAsync();
        var id = Seed(me, MatchStage.Shortlisted, portalKnowsIt: false);

        var response = await browser.PostAsync($"/api/matches/{id}/evaluate", null);

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
    }

    [Fact]
    public async Task Evaluate_somebody_elses_job_is_404_and_costs_nothing()
    {
        var (browser, _) = await SignInAsync();
        var (_, other) = await SignInAsync("zednik");
        var id = Seed(other, MatchStage.Shortlisted);

        var response = await browser.PostAsync($"/api/matches/{id}/evaluate", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, _factory.Llm.Calls);
    }

    // ---------- write a letter ----------

    [Fact]
    public async Task Letter_is_written_and_returned_in_the_detail()
    {
        var (browser, me) = await SignInAsync();
        var id = Seed(me, MatchStage.Evaluated);

        var response = await browser.PostAsync($"/api/matches/{id}/letter", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Letter, (await response.Content.ReadFromJsonAsync<Detail>())!.CoverLetter);
    }

    [Fact]
    public async Task Letter_for_a_job_not_evaluated_yet_is_409()
    {
        var (browser, me) = await SignInAsync();
        var id = Seed(me, MatchStage.Shortlisted);

        var response = await browser.PostAsync($"/api/matches/{id}/letter", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Existing_letter_is_409_unless_replace()
    {
        var (browser, me) = await SignInAsync();
        var id = Seed(me, MatchStage.Evaluated, letter: "Můj upravený dopis");

        var keep = await browser.PostAsync($"/api/matches/{id}/letter", null);
        var replace = await browser.PostAsync($"/api/matches/{id}/letter?replace=true", null);

        Assert.Equal(HttpStatusCode.Conflict, keep.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replace.StatusCode);
        Assert.Equal(Letter, (await replace.Content.ReadFromJsonAsync<Detail>())!.CoverLetter);
    }

    [Fact]
    public async Task Cut_off_letter_is_502()
    {
        var (browser, me) = await SignInAsync();
        var id = Seed(me, MatchStage.Evaluated);
        _factory.AiAnswers[LlmPurpose.CoverLetter] = ("Dobrý den, reaguji na", "max_tokens");

        var response = await browser.PostAsync($"/api/matches/{id}/letter", null);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    // ---------- edit the letter ----------

    [Fact]
    public async Task Edited_letter_is_saved()
    {
        var (browser, me) = await SignInAsync();
        var id = Seed(me, MatchStage.Evaluated, letter: Letter);

        var response = await browser.PutAsJsonAsync($"/api/matches/{id}/letter", new { text = "Dobrý den, upraveno." });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var detail = await browser.GetFromJsonAsync<Detail>($"/api/matches/{id}");
        Assert.Equal("Dobrý den, upraveno.", detail!.CoverLetter);
    }

    [Fact]
    public async Task Empty_edited_letter_is_400()
    {
        var (browser, me) = await SignInAsync();
        var id = Seed(me, MatchStage.Evaluated, letter: Letter);

        var response = await browser.PutAsJsonAsync($"/api/matches/{id}/letter", new { text = "  " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Editing_somebody_elses_letter_is_404()
    {
        var (browser, _) = await SignInAsync();
        var (_, other) = await SignInAsync("zednik");
        var id = Seed(other, MatchStage.Evaluated, letter: Letter);

        var response = await browser.PutAsJsonAsync($"/api/matches/{id}/letter", new { text = "Cizí" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
