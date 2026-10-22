using JobScout.Application.Jobs;
using JobScout.Application.Llm;
using JobScout.Core.Domain;
using JobScout.Infrastructure.Persistence;
using JobScout.Tests.TestUtils;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Tests.Application.Jobs;

/// <summary>
/// Use case: profile → AI suggests catalogue names → learned catalogue / source verifies → plan stored with the profile.
/// The plan is built once per profile version; the morning run only reads it.
/// </summary>
public sealed class PlanSearchTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 7, 8, 0, 0, TimeSpan.Zero);

    private const string Suggestions = """
        {
          "mappings": [
            { "profession": "Autolakýrník", "labels": ["Lakýrník", "Autolakýrník"] },
            { "profession": "Smart repair technik", "labels": ["Smart repair technik"] }
          ]
        }
        """;

    private readonly SqliteTestDatabase _database = new();
    private readonly FixedTimeProvider _time = new(T0);
    private readonly FakeLlmClient _llm = new(Suggestions);
    private readonly FakeJobSource _jobsCz = new("jobs.cz");
    private readonly Guid _profileId;

    public PlanSearchTests()
    {
        _jobsCz.Existing["Lakýrník"] = "lakyrnik";

        using var db = _database.CreateContext();
        var user = new User("dev", "autolakyrnik", "", "autolakyrnik", T0);
        var version = new UserProfile(user.Id, 1, "cv.md", null, "HASH", "{}", "fake-model", T0);
        db.AddRange(user, version);
        db.SaveChanges();
        _profileId = version.Id;
    }

    public void Dispose() => _database.Dispose();

    private static readonly string[] Professions = ["Autolakýrník", "Smart repair technik"];

    /// <summary>One run = one DbContext, the profile version loaded from it (tracked, like in the Worker scope).</summary>
    private async Task<SearchPlanResult> RunAsync(params IJobSource[] sources)
    {
        await using var db = _database.CreateContext();
        var version = await db.UserProfiles.SingleAsync(p => p.Id == _profileId);
        var useCase = new PlanSearch(db, new CategoryResolver(db, _time), new CategorySuggester(_llm),
            sources.Length == 0 ? [_jobsCz] : sources);

        return await useCase.ExecuteAsync(version, TestProfiles.With(Professions), CancellationToken.None);
    }

    private string? StoredPlanJson()
    {
        using var db = _database.CreateContext();
        return db.UserProfiles.Single(p => p.Id == _profileId).SearchPlanJson;
    }

    // ---------- First plan ----------

    [Fact]
    public async Task Plan_uses_verified_category_and_free_text_fallback()
    {
        var result = await RunAsync();

        Assert.True(result.IsNew);
        var searches = result.Plan.For("jobs.cz");
        Assert.Equal(
            new[]
            {
                new PlannedSearch("lakyrnik", null, "Kolín", 30),
                new PlannedSearch(null, "Smart repair technik", "Kolín", 30),
                new PlannedSearch(null, "Autolakýrník", "Kolín", 30),        // the title as text too (plan v4)
            },
            searches.ToArray());
    }

    [Fact]
    public async Task Plan_is_stored_with_the_profile_version()
    {
        var result = await RunAsync();

        var json = StoredPlanJson();
        Assert.NotNull(json);
        var stored = LlmJson.Deserialize<SearchPlan>(json);
        Assert.Equal(SearchPlan.CurrentVersion, stored.Version);
        Assert.Equal(result.Plan.For("jobs.cz").ToArray(), stored.For("jobs.cz").ToArray());
    }

    [Fact]
    public async Task Ai_gets_professions_and_known_categories_and_uses_the_smart_model()
    {
        await RunAsync();

        var request = Assert.Single(_llm.Requests);
        Assert.Equal(LlmTier.Smart, request.Tier);
        Assert.Equal(LlmPurpose.SearchPlan, request.Purpose);   // Haiku invented unrelated professions (7 Oct)
        Assert.Contains("Autolakýrník", request.UserPrompt);
        Assert.Contains("jobs.cz", request.UserPrompt);
        Assert.NotNull(request.JsonSchema);
    }

    [Fact]
    public async Task Every_label_and_the_profession_itself_are_verified_and_remembered()
    {
        await RunAsync();

        // Lakýrník ✔, Autolakýrník ✘, Smart repair technik ✘ – each asked once
        Assert.Equal(3, _jobsCz.ResolveRequests.Count);
        await using var db = _database.CreateContext();
        Assert.Equal(3, await db.SourceCategories.CountAsync());
    }

    // ---------- Stored plan is reused ----------

    [Fact]
    public async Task Second_run_reads_the_stored_plan_without_ai_or_requests()
    {
        await RunAsync();
        var second = await RunAsync();

        Assert.False(second.IsNew);
        Assert.Empty(second.Calls);
        Assert.Single(_llm.Requests);
        Assert.Equal(3, _jobsCz.ResolveRequests.Count);
        Assert.Equal(3, second.Plan.For("jobs.cz").Count);
    }

    [Fact]
    public async Task Plan_from_an_older_planner_version_is_rebuilt()
    {
        await using (var db = _database.CreateContext())
        {
            var version = await db.UserProfiles.SingleAsync(p => p.Id == _profileId);
            version.SetSearchPlan("""{"version":0,"sources":{}}""");
            await db.SaveChangesAsync();
        }

        var result = await RunAsync();

        Assert.True(result.IsNew);
        Assert.Equal(3, result.Plan.For("jobs.cz").Count);
    }

    [Fact]
    public async Task Another_profile_needing_the_same_professions_asks_the_portal_nothing()
    {
        await RunAsync();
        await using (var db = _database.CreateContext())
        {
            var version = await db.UserProfiles.SingleAsync(p => p.Id == _profileId);
            version.SetSearchPlan("""{"version":0,"sources":{}}""");   // force a new plan (e.g. new profile version)
            await db.SaveChangesAsync();
        }

        await RunAsync();

        Assert.Equal(3, _jobsCz.ResolveRequests.Count);   // learned catalogue answered everything
        Assert.Equal(2, _llm.Requests.Count);              // the AI is still asked for names (cheap, once per profile version)
        Assert.Contains("Lakýrník", _llm.Requests[1].UserPrompt);   // …and now sees the verified category
    }

    // ---------- Several sources ----------

    [Fact]
    public async Task Each_source_gets_its_own_plan()
    {
        var praceCz = new FakeJobSource("prace.cz");   // knows nothing → free text only

        var result = await RunAsync(_jobsCz, praceCz);

        Assert.Equal(2, result.Calls.Count);
        Assert.Contains(result.Plan.For("jobs.cz"), s => s.Category == "lakyrnik");
        Assert.All(result.Plan.For("prace.cz"), s => Assert.Null(s.Category));
        Assert.Empty(result.Plan.For("neznamy.cz"));
    }

    // ---------- Robustness ----------

    [Fact]
    public async Task Profession_missing_in_ai_answer_is_still_checked_by_its_own_name()
    {
        var llm = new FakeLlmClient("""{ "mappings": [] }""");
        _jobsCz.Existing["Autolakýrník"] = "autolakyrnik";

        await using var db = _database.CreateContext();
        var version = await db.UserProfiles.SingleAsync(p => p.Id == _profileId);
        var result = await new PlanSearch(db, new CategoryResolver(db, _time), new CategorySuggester(llm), [_jobsCz])
            .ExecuteAsync(version, TestProfiles.With(Professions), CancellationToken.None);

        Assert.Contains(result.Plan.For("jobs.cz"), s => s.Category == "autolakyrnik");
    }

    [Fact]
    public async Task Portal_outage_stores_no_plan()
    {
        _jobsCz.FailWith = new HttpRequestException("503");

        await Assert.ThrowsAsync<HttpRequestException>(() => RunAsync());

        Assert.Null(StoredPlanJson());   // next run tries again instead of keeping a crippled plan
    }
}
