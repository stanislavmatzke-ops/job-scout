using System.Text;
using JobScout.Application.Llm;
using JobScout.Application.Profiles;
using JobScout.Core.Domain;
using JobScout.Tests.TestUtils;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Tests.Application.Profiles;

/// <summary>
/// Use case "analyse once": the AI is called only for a new user or when CV / notes change.
/// Each ExecuteAsync runs with a fresh DbContext – like separate morning runs of the Worker.
/// </summary>
public sealed class AnalyzeProfileTests : IDisposable
{
    private const string ProfileJson = """
        {
          "headline": "Autolakýrník s 12 lety praxe",
          "yearsOfExperience": 12,
          "seniority": "senior",
          "targetRoles": ["autolakýrník", "vedoucí lakovny"],
          "coreSkills": ["lakování", "míchání barev"],
          "otherSkills": ["smart repair"],
          "domains": ["autoservis"],
          "languages": ["čeština rodilý mluvčí"],
          "preferences": {
            "remoteWork": "onsite",
            "acceptableRemoteWork": ["onsite"],
            "employmentTypes": ["employee"],
            "minMonthlySalaryCzk": 42000,
            "priorities": ["ranní směny"],
            "avoid": ["noční směny"]
          },
          "search": {
            "professions": ["autolakýrník", "lakýrník automobilů", "vedoucí lakovny"],
            "keywords": ["Standox"],
            "homeLocation": "Kolín",
            "commuteRadiusKm": 30,
            "extraLocations": []
          }
        }
        """;

    private static readonly DateTimeOffset T0 = new(2026, 10, 7, 6, 0, 0, TimeSpan.Zero);

    private readonly SqliteTestDatabase _database = new();
    private readonly FakeLlmClient _llm = new(ProfileJson);
    private readonly FixedTimeProvider _time = new(T0);

    public void Dispose() => _database.Dispose();

    private static ProfileInput Input(string subject = "autolakyrnik", string cv = "CV lakýrníka", string? notes = "Do 30 km od Kolína") =>
        new("dev", subject, subject, "cv.md", new LlmDocument("CV", "text/plain", Encoding.UTF8.GetBytes(cv)), notes);

    private async Task<AnalyzedProfile> RunAsync(ProfileInput input, FakeLlmClient? llm = null)
    {
        await using var db = _database.CreateContext();
        var useCase = new AnalyzeProfile(db, new ProfileExtractor(llm ?? _llm), _time);
        return await useCase.ExecuteAsync(input);
    }

    private long Count(string table) => (long)_database.Scalar($"SELECT COUNT(*) FROM \"{table}\"")!;

    // ---------- First run ----------

    [Fact]
    public async Task First_run_creates_the_user_and_profile_version_1()
    {
        var result = await RunAsync(Input());

        Assert.True(result.IsNew);
        Assert.Equal(1, _llm.Calls);
        Assert.Equal(1, result.Version.Version);
        Assert.Equal("dev", result.User.Provider);
        Assert.Equal("autolakyrnik", result.User.Subject);
        Assert.Equal(AccessStatus.Approved, result.User.Access);
        Assert.Equal(1, Count("Users"));
        Assert.Equal(1, Count("UserProfiles"));
    }

    [Fact]
    public async Task First_run_stores_the_profile_json_input_hash_model_and_time()
    {
        var result = await RunAsync(Input());

        await using var db = _database.CreateContext();
        var stored = await db.UserProfiles.SingleAsync();

        Assert.Equal(LlmJson.Serialize(result.Profile), stored.ProfileJson);
        Assert.Equal(Profile_hash(Input()), stored.InputHash);
        Assert.Equal("fake-model", stored.Model);
        Assert.Equal("cv.md", stored.CvFileName);
        Assert.Equal(T0, stored.CreatedAt);
        Assert.Equal(T0, (await db.Users.SingleAsync()).CreatedAt);
    }

    [Fact]
    public async Task First_run_sends_the_cv_as_a_document_and_the_notes_in_the_prompt()
    {
        await RunAsync(Input(notes: "Jen ranní směny"));

        var request = Assert.Single(_llm.Requests);
        Assert.Equal(LlmPurpose.Profile, request.Purpose);
        var document = Assert.Single(request.Documents);
        Assert.Equal("text/plain", document.MediaType);
        Assert.Contains("Jen ranní směny", request.UserPrompt);
        Assert.NotNull(request.JsonSchema);
    }

    [Fact]
    public async Task Ai_call_is_attributed_to_the_user_for_cost_tracking()
    {
        // The usage recorder (ADR-0016) reads the ambient context – the user exists only in memory at that moment
        LlmCallContext.Scope? seen = null;
        var llm = new FakeLlmClient(_ =>
        {
            seen = LlmCallContext.Current;
            return new LlmResponse(ProfileJson, "fake-model", "end_turn", 100, 50);
        });

        var result = await RunAsync(Input(), llm);

        Assert.NotNull(seen);
        Assert.Equal(result.User.Id, seen.UserId);
        Assert.Null(LlmCallContext.Current);              // restored after the use case
    }

    // ---------- Analyse once ----------

    [Fact]
    public async Task Same_input_does_not_call_the_ai_again()
    {
        var first = await RunAsync(Input());
        var second = await RunAsync(Input());

        Assert.Equal(1, _llm.Calls);                 // the whole point: zero tokens on the second run
        Assert.False(second.IsNew);
        Assert.Null(second.Response);
        Assert.Equal(first.Version.Id, second.Version.Id);
        Assert.Equal(first.User.Id, second.User.Id);
        Assert.Equal(1, Count("UserProfiles"));
    }

    [Fact]
    public async Task Stored_profile_is_read_back_identical_to_the_analysed_one()
    {
        var first = await RunAsync(Input());
        var second = await RunAsync(Input());

        // Records with lists compare lists by reference → compare the serialized form
        Assert.Equal(LlmJson.Serialize(first.Profile), LlmJson.Serialize(second.Profile));
        Assert.Equal(42000, second.Profile.Preferences.MinMonthlySalaryCzk);
        Assert.Equal(Seniority.Senior, second.Profile.Seniority);
        Assert.Equal("Kolín", second.Profile.Search.HomeLocation);
        Assert.Equal(30, second.Profile.Search.CommuteRadiusKm);
    }

    [Fact]
    public async Task Whitespace_around_notes_is_not_a_change()
    {
        await RunAsync(Input(notes: "Do 30 km od Kolína"));
        var second = await RunAsync(Input(notes: "  Do 30 km od Kolína \r\n"));

        Assert.False(second.IsNew);
        Assert.Equal(1, _llm.Calls);
    }

    // ---------- Changed input → new version ----------

    [Fact]
    public async Task Changed_notes_create_version_2_and_keep_version_1()
    {
        await RunAsync(Input(notes: "Do 30 km od Kolína"));
        _time.Now = T0.AddDays(1);
        var second = await RunAsync(Input(notes: "Do 50 km od Kolína, i Praha"));

        Assert.True(second.IsNew);
        Assert.Equal(2, _llm.Calls);
        Assert.Equal(2, second.Version.Version);
        Assert.Equal(T0.AddDays(1), second.Version.CreatedAt);
        Assert.Equal(2, Count("UserProfiles"));      // old versions stay – JobMatch remembers which one evaluated it
        Assert.Equal(1, Count("Users"));
    }

    [Fact]
    public async Task Changed_cv_creates_a_new_version()
    {
        await RunAsync(Input(cv: "CV v1"));
        var second = await RunAsync(Input(cv: "CV v2 – nový kurz Standox"));

        Assert.True(second.IsNew);
        Assert.Equal(2, second.Version.Version);
    }

    [Fact]
    public async Task Removing_the_notes_is_a_change()
    {
        await RunAsync(Input(notes: "Do 30 km od Kolína"));
        var second = await RunAsync(Input(notes: null));

        Assert.True(second.IsNew);
        Assert.Equal(2, second.Version.Version);
    }

    [Fact]
    public async Task Going_back_to_older_input_creates_a_new_version_not_a_reuse()
    {
        // Only the LATEST version is compared – simple and predictable; v3 = same content as v1
        await RunAsync(Input(notes: "A"));
        await RunAsync(Input(notes: "B"));
        var third = await RunAsync(Input(notes: "A"));

        Assert.Equal(3, third.Version.Version);
        Assert.Equal(3, _llm.Calls);
    }

    // ---------- Users ----------

    [Fact]
    public async Task Different_personas_are_different_users_with_their_own_versions()
    {
        var painter = await RunAsync(Input(subject: "autolakyrnik"));
        var bricklayer = await RunAsync(Input(subject: "zednik"));

        Assert.NotEqual(painter.User.Id, bricklayer.User.Id);
        Assert.Equal(1, painter.Version.Version);
        Assert.Equal(1, bricklayer.Version.Version);   // versions are numbered per user
        Assert.Equal(2, _llm.Calls);                   // same CV text, but another person → own analysis
    }

    [Fact]
    public async Task Same_subject_from_another_provider_is_another_user()
    {
        await RunAsync(Input() with { Provider = "dev" });
        await RunAsync(Input() with { Provider = "google" });

        Assert.Equal(2, Count("Users"));
    }

    // ---------- Failures ----------

    [Fact]
    public async Task Failed_ai_call_leaves_no_user_without_a_profile()
    {
        var failing = new FakeLlmClient(_ => throw new HttpRequestException("API nedostupné"));

        await Assert.ThrowsAsync<HttpRequestException>(() => RunAsync(Input(), failing));

        Assert.Equal(0, Count("Users"));     // user and profile are saved together in one SaveChanges
        Assert.Equal(0, Count("UserProfiles"));
    }

    [Fact]
    public async Task Truncated_ai_answer_is_rejected_and_nothing_is_stored()
    {
        var truncated = new FakeLlmClient("{\"headline\": \"Autolak", stopReason: "max_tokens");

        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(Input(), truncated));

        Assert.Equal(0, Count("UserProfiles"));
    }

    [Fact]
    public async Task Failed_reanalysis_keeps_the_previous_version_active()
    {
        await RunAsync(Input(notes: "A"));
        var failing = new FakeLlmClient(_ => throw new HttpRequestException("API nedostupné"));

        await Assert.ThrowsAsync<HttpRequestException>(() => RunAsync(Input(notes: "B"), failing));
        var again = await RunAsync(Input(notes: "A"));

        Assert.False(again.IsNew);
        Assert.Equal(1, again.Version.Version);
    }

    private static string Profile_hash(ProfileInput input) =>
        JobScout.Core.Domain.UserProfile.ComputeInputHash(input.Cv.Content, input.Notes, ProfilePrompts.Version);
}
