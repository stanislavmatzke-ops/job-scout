using System.Net;
using System.Net.Http.Json;
using JobScout.Application.Llm;
using JobScout.Core.Domain;
using JobScout.Core.Jobs;
using JobScout.Tests.TestUtils;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Tests.Api;

/// <summary>
/// 13 Oct over HTTP: a reached AI limit is 429 with the reason and Retry-After (ADR-0021), /api/me/limits says what is left;
/// "Zpětná vazba" is open to everyone inside the app, the admin reads it.
/// </summary>
public sealed class AiLimitAndFeedbackEndpointsTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 10, 6, 0, 0, TimeSpan.Zero);

    private readonly JobScoutApiFactory _factory = new(settings: new Dictionary<string, string>
    {
        ["Auth:Admins:0"] = "dev:boss",
    });

    public AiLimitAndFeedbackEndpointsTests()
    {
        _factory.AiAnswers[LlmPurpose.Evaluation] = (MatchSeeder.EvaluationJson(6, "Částečná shoda."), "end_turn");
    }

    public void Dispose() => _factory.Dispose();

    private sealed record Me(Guid Id);
    private sealed record Usage(int Used, int Limit, int Left, DateTimeOffset? ResetsAt);
    private sealed record Limits(bool Unlimited, Usage Profile, Usage CoverLetters, Usage Evaluations, int LetterRewritesPerJob,
        decimal CostUsd, decimal CostLimitUsd, bool Paused);
    private sealed record Problem(int Status, string Detail, string? Reason, int? Limit, DateTimeOffset? RetryAt);
    private sealed record FeedbackRow(string UserName, string? Email, string Kind, int? Rating, string Message, bool ReplyWanted);

    private async Task<(HttpClient Browser, Guid UserId)> SignInAsync(string persona = "private")
    {
        var browser = _factory.CreateBrowser();
        await browser.PostAsync($"/api/auth/dev/{persona}", null);
        return (browser, (await browser.GetFromJsonAsync<Me>("/api/me"))!.Id);
    }

    /// <summary>Clicks made earlier – as the usage recorder (ADR-0016) stores them.</summary>
    private async Task ClickedAsync(Guid userId, LlmPurpose purpose, int times)
    {
        await using var db = _factory.CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < times; i++)
            db.LlmCalls.Add(LlmCall.Success(userId, null, purpose.ToString(), "fake-model", 100, 50, 1_000, 100, now.AddMinutes(-10 - i)));
        await db.SaveChangesAsync();
    }

    private Guid SeedShortlisted(Guid userId)
    {
        using var db = _factory.CreateDbContext();
        ProfileSeeder.Add(db, userId, T0);
        var id = MatchSeeder.Add(db, userId, "Solution Architect", T0, MatchStage.Shortlisted, triage: 2);
        var offer = db.JobMatches.Include(m => m.JobOffer).Single(m => m.Id == id).JobOffer!;
        _factory.JobsCz.Details[offer.ExternalId] = new JobDetail("jobs.cz", offer.ExternalId, offer.Url, offer.Title,
            offer.Company, "Hledáme architekta.", true);
        return id;
    }

    // ---------- AI limits ----------

    [Fact]
    public async Task Limits_say_what_is_left()
    {
        var (browser, me) = await SignInAsync();
        await ClickedAsync(me, LlmPurpose.Evaluation, 3);

        var limits = await browser.GetFromJsonAsync<Limits>("/api/me/limits");

        Assert.False(limits!.Unlimited);
        Assert.Equal((3, 20, 17), (limits.Evaluations.Used, limits.Evaluations.Limit, limits.Evaluations.Left));
        Assert.Equal((0, 3), (limits.Profile.Used, limits.Profile.Limit));
        Assert.Equal(2, limits.LetterRewritesPerJob);
    }

    [Fact]
    public async Task The_admin_has_no_limits()
    {
        var (browser, _) = await SignInAsync("boss");

        Assert.True((await browser.GetFromJsonAsync<Limits>("/api/me/limits"))!.Unlimited);
    }

    [Fact]
    public async Task Over_the_limit_is_429_with_the_reason_and_no_ai_call()
    {
        var (browser, me) = await SignInAsync();
        var id = SeedShortlisted(me);
        await ClickedAsync(me, LlmPurpose.Evaluation, 20);

        var response = await browser.PostAsync($"/api/matches/{id}/evaluate", null);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.NotNull(response.Headers.RetryAfter);
        var problem = await response.Content.ReadFromJsonAsync<Problem>();
        Assert.Equal(("Evaluation", (int?)20), (problem!.Reason, problem.Limit));
        Assert.NotNull(problem.RetryAt);
        Assert.Contains("20×", problem.Detail);
        Assert.Equal(0, _factory.Llm.Calls);
    }

    [Fact]
    public async Task Under_the_limit_the_button_works_as_before()
    {
        var (browser, me) = await SignInAsync();
        var id = SeedShortlisted(me);
        await ClickedAsync(me, LlmPurpose.Evaluation, 19);

        var response = await browser.PostAsync($"/api/matches/{id}/evaluate", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------- feedback ----------

    [Fact]
    public async Task Feedback_is_accepted_and_the_admin_sees_it()
    {
        var (browser, _) = await SignInAsync();

        var sent = await browser.PostAsJsonAsync("/api/feedback",
            new { kind = "Problem", rating = 3, message = "Tlačítko Uložit nereaguje.", replyWanted = true });
        Assert.Equal(HttpStatusCode.NoContent, sent.StatusCode);

        var (admin, _) = await SignInAsync("boss");
        var rows = await admin.GetFromJsonAsync<List<FeedbackRow>>("/api/admin/feedback");
        var row = Assert.Single(rows!);
        Assert.Equal(("private", "Problem", (int?)3, "Tlačítko Uložit nereaguje."), (row.UserName, row.Kind, row.Rating, row.Message));
        Assert.False(row.ReplyWanted);                  // the dev persona has no e-mail – nobody to answer to
    }

    [Fact]
    public async Task Feedback_without_a_kind_or_text_is_400()
    {
        var (browser, _) = await SignInAsync();

        Assert.Equal(HttpStatusCode.BadRequest,
            (await browser.PostAsJsonAsync("/api/feedback", new { message = "Ahoj" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await browser.PostAsJsonAsync("/api/feedback", new { kind = "Idea", message = "  " })).StatusCode);
    }

    [Fact]
    public async Task Only_the_admin_reads_feedback()
    {
        var (browser, _) = await SignInAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await browser.GetAsync("/api/admin/feedback")).StatusCode);
    }

    [Fact]
    public async Task Feedback_needs_sign_in()
    {
        var response = await _factory.CreateBrowser().PostAsJsonAsync("/api/feedback", new { kind = "Idea", message = "Ahoj" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
