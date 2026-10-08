using System.Text.Json;
using System.Text.RegularExpressions;
using JobScout.Application.Llm;
using JobScout.Application.Triage;
using JobScout.Core.Domain;
using JobScout.Core.Jobs;
using JobScout.Tests.TestUtils;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Tests.Application.Triage;

/// <summary>
/// Cheap first sieve over titles (Haiku, all pending jobs in one call per batch):
/// clear misfits are triaged out with a reason, everything else – including anything unclear – is shortlisted.
/// </summary>
public sealed partial class TriageJobsTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 8, 6, 0, 0, TimeSpan.Zero);

    private readonly SqliteTestDatabase _database = new();
    private readonly FixedTimeProvider _time = new(T0);
    private readonly Guid _userId;
    private readonly Guid _otherUserId;
    private int _nextId;

    public TriageJobsTests()
    {
        using var db = _database.CreateContext();
        var user = new User("dev", "zednik", "", "zednik", T0);
        var other = new User("dev", "autolakyrnik", "", "autolakyrnik", T0);
        db.AddRange(user, other);
        db.SaveChanges();
        (_userId, _otherUserId) = (user.Id, other.Id);
    }

    public void Dispose() => _database.Dispose();

    // ---------- helpers ----------

    [GeneratedRegex(@"^(\d+)\. (.+)$", RegexOptions.Multiline)]
    private static partial Regex NumberedLine();

    /// <summary>
    /// Fake AI reading the numbered list from the prompt: lines containing a marker get relevance 1 ("Jiná profese"),
    /// all others relevance 4.
    /// </summary>
    private static FakeLlmClient DroppingTitlesWith(params string[] markers) => new(request =>
    {
        var decisions = NumberedLine().Matches(request.UserPrompt)
            .Select(m =>
            {
                var drop = markers.Any(x => m.Groups[2].Value.Contains(x, StringComparison.OrdinalIgnoreCase));
                return new { id = m.Groups[1].Value, relevance = drop ? 1 : 4, reason = drop ? "Jiná profese" : "" };
            });
        var json = JsonSerializer.Serialize(new { decisions });
        return new LlmResponse(json, "fake-haiku", "end_turn", 500, 50);
    });

    private void Seed(Guid userId, params string[] titles) => SeedWithStage(userId, null, titles);

    private void SeedWithStage(Guid userId, Action<JobMatch>? arrange, params string[] titles)
    {
        using var db = _database.CreateContext();
        foreach (var title in titles)
        {
            var id = $"2001{++_nextId:D6}";
            var offer = JobOffer.FromListing(new JobListing("jobs.cz", id, title, "Firma", "Ostrava", $"https://www.jobs.cz/rpd/{id}/", ["Plný úvazek"]), T0);
            var match = new JobMatch(userId, offer.Id, 2, T0);
            arrange?.Invoke(match);
            db.AddRange(offer, match);
        }
        db.SaveChanges();
    }

    private async Task<TriageResult> TriageAsync(FakeLlmClient llm, Guid? userId = null)
    {
        await using var db = _database.CreateContext();
        return await new TriageJobs(db, llm, _time)
            .ExecuteAsync(userId ?? _userId, TestProfiles.With(["Zedník", "Stavbyvedoucí"], "Opava", 40));
    }

    private async Task<List<JobMatch>> MatchesAsync(Guid? userId = null)
    {
        await using var db = _database.CreateContext();
        return await db.JobMatches.Include(m => m.JobOffer).Where(m => m.UserId == (userId ?? _userId)).ToListAsync();
    }

    private async Task<MatchStage> StageOfAsync(string title) =>
        (await MatchesAsync()).Single(m => m.JobOffer!.Title == title).Stage;

    // ---------- decisions ----------

    [Fact]
    public async Task Misfits_are_triaged_out_with_reason_the_rest_is_shortlisted()
    {
        Seed(_userId, "Stavbyvedoucí", "Senior statik Inženýr", "Zedník – rekonstrukce");

        var result = await TriageAsync(DroppingTitlesWith("statik"));

        Assert.Equal(2, result.Kept);
        Assert.Equal(1, result.Dropped);
        Assert.Equal(MatchStage.Shortlisted, await StageOfAsync("Stavbyvedoucí"));
        Assert.Equal(MatchStage.Shortlisted, await StageOfAsync("Zedník – rekonstrukce"));
        var statik = (await MatchesAsync()).Single(m => m.JobOffer!.Title.Contains("statik"));
        Assert.Equal(MatchStage.TriagedOut, statik.Stage);
        Assert.Equal("Jiná profese", statik.TriageReason);
        Assert.Equal(1, statik.TriageScore);
        Assert.Equal(4, (await MatchesAsync()).Single(m => m.JobOffer!.Title == "Stavbyvedoucí").TriageScore);
    }

    [Theory]
    [InlineData(5, MatchStage.Shortlisted)]
    [InlineData(3, MatchStage.Shortlisted)]     // "maybe" stays
    [InlineData(2, MatchStage.Shortlisted)]     // "theoretically makes sense" stays too – at the end of the queue (8 Oct)
    [InlineData(1, MatchStage.TriagedOut)]      // only clear nonsense is dropped
    public async Task Only_relevance_1_is_triaged_out(int relevance, MatchStage expected)
    {
        Seed(_userId, "Nabídka");
        var llm = new FakeLlmClient($$"""{ "decisions": [ { "id": "1", "relevance": {{relevance}}, "reason": "Důvod" } ] }""");

        await TriageAsync(llm);

        var match = Assert.Single(await MatchesAsync());
        Assert.Equal(expected, match.Stage);
        Assert.Equal(relevance, match.TriageScore);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(-1)]
    public async Task Relevance_outside_the_scale_is_treated_as_unsure(int relevance)
    {
        Seed(_userId, "Nabídka");
        var llm = new FakeLlmClient($$"""{ "decisions": [ { "id": "1", "relevance": {{relevance}}, "reason": "Důvod" } ] }""");

        await TriageAsync(llm);

        var match = Assert.Single(await MatchesAsync());
        Assert.Equal(MatchStage.Shortlisted, match.Stage);
        Assert.Equal(TriageJobs.DefaultRelevance, match.TriageScore);
    }

    [Fact]
    public async Task Job_missing_in_the_ai_answer_is_kept()
    {
        // When in doubt, keep: dropping a good job is worse than one extra evaluation
        Seed(_userId, "Stavbyvedoucí", "Projektový manažer");
        var llm = new FakeLlmClient("""{ "decisions": [ { "id": "1", "relevance": 5, "reason": "" } ] }""");

        var result = await TriageAsync(llm);

        Assert.Equal(2, result.Kept);
        Assert.All(await MatchesAsync(), m => Assert.Equal(MatchStage.Shortlisted, m.Stage));
        // Both were created at the same time, so which one is line 1 is not defined – check the pair of scores:
        // the answered job keeps its 5, the missing one becomes "unsure"
        Assert.Equal(new int?[] { TriageJobs.DefaultRelevance, 5 },
            (await MatchesAsync()).Select(m => m.TriageScore).Order().ToArray());
    }

    [Fact]
    public async Task Drop_without_a_reason_is_not_trusted()
    {
        Seed(_userId, "Stavbyvedoucí");
        var llm = new FakeLlmClient("""{ "decisions": [ { "id": "1", "relevance": 1, "reason": "  " } ] }""");

        await TriageAsync(llm);

        Assert.Equal(MatchStage.Shortlisted, await StageOfAsync("Stavbyvedoucí"));
        Assert.Equal(TriageJobs.DefaultRelevance, (await MatchesAsync()).Single().TriageScore);
    }

    [Fact]
    public async Task Unknown_ids_in_the_answer_are_ignored()
    {
        Seed(_userId, "Stavbyvedoucí");
        var llm = new FakeLlmClient("""{ "decisions": [ { "id": "99", "relevance": 1, "reason": "x" }, { "id": "1", "relevance": 4, "reason": "" } ] }""");

        var result = await TriageAsync(llm);

        Assert.Equal(1, result.Kept);
        Assert.Equal(0, result.Dropped);
    }

    // ---------- what gets triaged ----------

    [Fact]
    public async Task Only_jobs_in_stage_found_are_triaged()
    {
        Seed(_userId, "Nová nabídka");
        SeedWithStage(_userId, m => m.Shortlist(4, T0), "Už prošla");
        SeedWithStage(_userId, m => m.MarkTriagedOut("dřív vyřazena", T0), "Už vyřazena");
        SeedWithStage(_userId, m => m.ApplyEvaluation(8, "{}", T0), "Už ohodnocena");
        var llm = DroppingTitlesWith();

        var result = await TriageAsync(llm);

        Assert.Single(result.Jobs);
        Assert.DoesNotContain("Už prošla", llm.Requests[0].UserPrompt);
        Assert.Equal(MatchStage.Evaluated, await StageOfAsync("Už ohodnocena"));
    }

    [Fact]
    public async Task Second_run_has_nothing_to_triage_and_calls_no_ai()
    {
        Seed(_userId, "Stavbyvedoucí", "Statik");
        var llm = DroppingTitlesWith("statik");

        await TriageAsync(llm);
        var second = await TriageAsync(llm);

        Assert.Empty(second.Jobs);
        Assert.Single(llm.Requests);
    }

    [Fact]
    public async Task Nothing_pending_means_no_ai_call()
    {
        var llm = DroppingTitlesWith();

        var result = await TriageAsync(llm);

        Assert.Empty(result.Calls);
        Assert.Empty(llm.Requests);
    }

    [Fact]
    public async Task Other_users_jobs_are_not_touched()
    {
        Seed(_otherUserId, "Mechanik stavebních strojů");

        await TriageAsync(DroppingTitlesWith("mechanik"));

        Assert.Equal(MatchStage.Found, Assert.Single(await MatchesAsync(_otherUserId)).Stage);
    }

    // ---------- the AI call ----------

    [Fact]
    public async Task One_cheap_call_with_profile_and_numbered_jobs()
    {
        Seed(_userId, "Stavbyvedoucí", "Statik");
        var llm = DroppingTitlesWith();

        await TriageAsync(llm);

        var request = Assert.Single(llm.Requests);
        Assert.Equal(LlmTier.Fast, request.Tier);
        Assert.NotNull(request.JsonSchema);
        Assert.Contains("<profil_kandidata>", request.UserPrompt);
        Assert.Contains("Opava", request.UserPrompt);
        Assert.Equal(2, NumberedLine().Matches(request.UserPrompt).Count);
        Assert.Contains("Statik | Firma | Ostrava", request.UserPrompt);
    }

    [Fact]
    public async Task Many_jobs_are_split_into_batches_with_numbering_per_batch()
    {
        Seed(_userId, Enumerable.Range(1, TriageJobs.BatchSize + 5).Select(i => $"Nabídka {i}").ToArray());
        var llm = DroppingTitlesWith();

        var result = await TriageAsync(llm);

        Assert.Equal(2, llm.Requests.Count);
        Assert.Equal(TriageJobs.BatchSize, NumberedLine().Matches(llm.Requests[0].UserPrompt).Count);
        Assert.Equal(5, NumberedLine().Matches(llm.Requests[1].UserPrompt).Count);
        Assert.StartsWith("1. ", NumberedLine().Matches(llm.Requests[1].UserPrompt)[0].Value);
        Assert.Equal(TriageJobs.BatchSize + 5, result.Kept);
    }

    [Fact]
    public async Task Failure_in_a_later_batch_keeps_the_progress_of_earlier_ones()
    {
        Seed(_userId, Enumerable.Range(1, TriageJobs.BatchSize + 1).Select(i => $"Nabídka {i}").ToArray());
        var calls = 0;
        var llm = new FakeLlmClient(request => ++calls == 1
            ? DroppingTitlesWith().CompleteAsync(request).Result
            : throw new HttpRequestException("API nedostupné"));

        await Assert.ThrowsAsync<HttpRequestException>(() => TriageAsync(llm));

        var matches = await MatchesAsync();
        Assert.Equal(TriageJobs.BatchSize, matches.Count(m => m.Stage == MatchStage.Shortlisted));
        Assert.Equal(1, matches.Count(m => m.Stage == MatchStage.Found));     // next run continues here
    }

    [Fact]
    public async Task Truncated_answer_fails_and_changes_nothing()
    {
        Seed(_userId, "Stavbyvedoucí");
        var llm = new FakeLlmClient("""{ "decisions": [ { "id": "1", "rel""", stopReason: "max_tokens");

        await Assert.ThrowsAsync<InvalidOperationException>(() => TriageAsync(llm));

        Assert.Equal(MatchStage.Found, Assert.Single(await MatchesAsync()).Stage);
    }
}
