using JobScout.Application.Letters;
using JobScout.Application.Llm;
using JobScout.Core.Domain;
using JobScout.Core.Jobs;
using JobScout.Tests.TestUtils;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Tests.Application.Letters;

/// <summary>
/// Cover letter drafts for well-scored jobs (7+). The agent only prepares – the user edits and sends it himself.
/// </summary>
public sealed class WriteCoverLettersTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 8, 6, 0, 0, TimeSpan.Zero);
    private const string Letter = "Dobrý den,\n\nreaguji na Vaši nabídku…\n\nS pozdravem\n[Jméno a příjmení]";

    private readonly SqliteTestDatabase _database = new();
    private readonly FixedTimeProvider _time = new(T0);
    private readonly FakeLlmClient _llm = new(Letter);
    private readonly Guid _userId;
    private readonly Guid _otherUserId;
    private int _nextId;

    public WriteCoverLettersTests()
    {
        using var db = _database.CreateContext();
        var user = new User("dev", "private", "", "private", T0);
        var other = new User("dev", "zednik", "", "zednik", T0);
        db.AddRange(user, other);
        db.SaveChanges();
        (_userId, _otherUserId) = (user.Id, other.Id);
    }

    public void Dispose() => _database.Dispose();

    // ---------- helpers ----------

    private string Seed(string title, int? score, MatchStatus status = MatchStatus.New, Guid? userId = null, string? letter = null)
    {
        var id = $"2001{++_nextId:D6}";
        using var db = _database.CreateContext();
        var offer = JobOffer.FromListing(new JobListing("jobs.cz", id, title, "CloudNest s.r.o.", "Praha",
            $"https://www.jobs.cz/rpd/{id}/", ["Práce převážně z domova"]), T0);
        offer.SetDetail(new JobDetail("jobs.cz", id, offer.Url, title, offer.Company,
            $"Hledáme kolegu na pozici {title}. Požadujeme .NET, Azure a zkušenost s architekturou.", true), T0);

        var match = new JobMatch(userId ?? _userId, offer.Id, 2, T0);
        match.Shortlist(4, T0);
        if (score is not null)
            match.ApplyEvaluation(score.Value, $$"""{"score":{{score}},"summary":"Sedí na .NET a Azure.","reasons":["Azure"]}""", T0);
        if (status != MatchStatus.New)
            match.ChangeStatus(status, null, T0);
        if (letter is not null)
            match.SetCoverLetter(letter, T0);

        db.AddRange(offer, match);
        db.SaveChanges();
        return id;
    }

    private async Task<WriteCoverLettersResult> WriteAsync(int? maxPerRun = null, FakeLlmClient? llm = null)
    {
        await using var db = _database.CreateContext();
        return await new WriteCoverLetters(db, new CoverLetterWriter(llm ?? _llm), _time)
            .ExecuteAsync(_userId, TestProfiles.With(["Softwarový architekt"], "Praha", 0), maxPerRun ?? WriteCoverLetters.MaxPerRun);
    }

    private async Task<JobMatch> MatchAsync(string externalId)
    {
        await using var db = _database.CreateContext();
        return await db.JobMatches.Include(m => m.JobOffer).SingleAsync(m => m.JobOffer!.ExternalId == externalId);
    }

    // ---------- who gets a letter ----------

    [Fact]
    public async Task Letter_is_written_and_stored_for_a_job_scored_7_or_more()
    {
        var id = Seed("Softwarový architekt", score: 8);

        var result = await WriteAsync();

        Assert.Equal(Letter, Assert.Single(result.Letters).Letter);
        var match = await MatchAsync(id);
        Assert.Equal(Letter, match.CoverLetter);
        Assert.Equal(T0, match.UpdatedAt);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(1)]
    public async Task Job_below_7_gets_no_letter(int score)
    {
        Seed("Vývojář", score);

        var result = await WriteAsync();

        Assert.Empty(result.Letters);
        Assert.Empty(_llm.Requests);
    }

    [Fact]
    public async Task Exactly_7_gets_a_letter()
    {
        Seed("Tech lead", score: 7);

        Assert.Single((await WriteAsync()).Letters);
    }

    [Fact]
    public async Task Not_yet_evaluated_job_gets_no_letter()
    {
        Seed("Čeká na hodnocení", score: null);

        Assert.Empty((await WriteAsync()).Letters);
    }

    [Theory]
    [InlineData(MatchStatus.Hidden)]
    [InlineData(MatchStatus.Dismissed)]
    public async Task Job_the_user_threw_away_gets_no_letter(MatchStatus status)
    {
        // Don't pay for a letter the user will never read
        Seed("Architekt", score: 9, status: status);

        Assert.Empty((await WriteAsync()).Letters);
    }

    [Fact]
    public async Task Existing_letter_is_never_overwritten()
    {
        // The user may have edited it already
        var id = Seed("Architekt", score: 9, letter: "Můj upravený dopis");

        await WriteAsync();

        Assert.Equal("Můj upravený dopis", (await MatchAsync(id)).CoverLetter);
        Assert.Empty(_llm.Requests);
    }

    [Fact]
    public async Task Other_users_jobs_are_not_touched()
    {
        Seed("Stavbyvedoucí", score: 9, userId: _otherUserId);

        Assert.Empty((await WriteAsync()).Letters);
    }

    // ---------- limits and order ----------

    [Fact]
    public async Task Best_jobs_first_and_the_rest_waits()
    {
        Seed("Sedm", 7);
        Seed("Devět", 9);
        Seed("Osm", 8);

        var first = await WriteAsync(maxPerRun: 2);
        var second = await WriteAsync(maxPerRun: 2);

        Assert.Equal(new[] { "Devět", "Osm" }, first.Letters.Select(l => l.Offer.Title).ToArray());
        Assert.Equal(1, first.Remaining);
        Assert.Equal("Sedm", Assert.Single(second.Letters).Offer.Title);
        Assert.Equal(0, second.Remaining);
    }

    [Fact]
    public async Task Second_run_writes_nothing_again()
    {
        Seed("Architekt", 9);

        await WriteAsync();
        var second = await WriteAsync();

        Assert.Empty(second.Letters);
        Assert.Single(_llm.Requests);
    }

    // ---------- the AI call ----------

    [Fact]
    public async Task Prompt_has_profile_evaluation_and_job_and_asks_for_plain_text()
    {
        Seed("Softwarový architekt", score: 8);

        await WriteAsync();

        var request = Assert.Single(_llm.Requests);
        Assert.Equal(LlmTier.Smart, request.Tier);
        Assert.Equal(LlmPurpose.CoverLetter, request.Purpose);
        Assert.Null(request.JsonSchema);                        // a letter is prose, not JSON
        Assert.Contains("<profil_kandidata>", request.UserPrompt);
        Assert.Contains("Sedí na .NET a Azure.", request.UserPrompt);
        Assert.Contains("<nabidka>", request.UserPrompt);
        Assert.Contains("Požadujeme .NET, Azure", request.UserPrompt);
        Assert.Contains("CloudNest s.r.o.", request.UserPrompt);
    }

    [Fact]
    public async Task Truncated_letter_is_not_stored_and_does_not_stop_the_run()
    {
        // A bad letter is not worth failing the whole morning run – evaluations are already saved (8 Oct)
        var id = Seed("Architekt", 9);
        var llm = new FakeLlmClient("Dobrý den,\n\nreaguji na", stopReason: "max_tokens");

        var result = await WriteAsync(llm: llm);

        Assert.Null((await MatchAsync(id)).CoverLetter);   // stays pending – next run tries again
        Assert.Empty(result.Letters);
        Assert.Equal(1, result.Failed);
        Assert.Equal(1, result.Remaining);
    }

    [Fact]
    public async Task Empty_letter_is_not_stored()
    {
        var id = Seed("Architekt", 9);
        var llm = new FakeLlmClient("   ");

        var result = await WriteAsync(llm: llm);

        Assert.Null((await MatchAsync(id)).CoverLetter);
        Assert.Equal(1, result.Failed);
    }

    [Fact]
    public async Task One_bad_letter_does_not_prevent_the_others()
    {
        Seed("Devět", 9);
        Seed("Osm", 8);
        var calls = 0;
        var llm = new FakeLlmClient(_ => ++calls == 1
            ? new LlmResponse("Dobrý den, reaguji na", "fake", "max_tokens", 5000, 2000)
            : new LlmResponse(Letter, "fake", "end_turn", 5000, 500));

        var result = await WriteAsync(llm: llm);

        Assert.Single(result.Letters);
        Assert.Equal(1, result.Failed);
        Assert.Equal(2, result.Calls.Count);                // both calls are paid – visible for cost tracking
    }

    [Fact]
    public async Task Api_outage_still_fails_the_run()
    {
        // Not a content problem – the run is marked Failed and the window doesn't move (ScoutRun)
        Seed("Architekt", 9);
        var llm = new FakeLlmClient(_ => throw new HttpRequestException("API nedostupné"));

        await Assert.ThrowsAsync<HttpRequestException>(() => WriteAsync(llm: llm));
    }

    [Fact]
    public async Task Letter_has_enough_room_for_czech()
    {
        // Czech needs noticeably more tokens per word than English – 1200 was too tight (8 Oct)
        Seed("Architekt", 9);

        await WriteAsync();

        Assert.True(Assert.Single(_llm.Requests).MaxTokens >= 2000);
    }

    [Fact]
    public async Task Letter_text_is_trimmed()
    {
        var id = Seed("Architekt", 9);
        var llm = new FakeLlmClient($"\n\n{Letter}\n  ");

        await WriteAsync(llm: llm);

        Assert.Equal(Letter, (await MatchAsync(id)).CoverLetter);
    }
}
