using JobScout.Application.Llm;
using JobScout.Application.Profiles;
using JobScout.Application.Scouting;
using JobScout.Application.Users;
using JobScout.Core.Domain;
using JobScout.Tests.TestUtils;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Tests.Application.Scouting;

/// <summary>
/// The morning run for everybody (5f-2): who gets it (approved, with a profile – ADR-0017) and which profile is used
/// (from the DB, re-analysed only when CV, notes or the prompt version changed).
/// </summary>
public sealed class DailyScoutTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 12, 5, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Cv = "# Jana Ukázková\nSenior .NET vývojářka"u8.ToArray();

    private readonly SqliteTestDatabase _database = new();
    private readonly FixedTimeProvider _time = new(T0);
    private readonly FakeLlmClient _llm = new(LlmJson.Serialize(TestProfiles.With(["Softwarový architekt"])));

    public void Dispose() => _database.Dispose();

    private Guid AddUser(string subject, AccessStatus access = AccessStatus.Approved, bool withProfile = true,
        string provider = "google", int minutes = 0)
    {
        using var db = _database.CreateContext();
        var user = new User(provider, subject, "", subject, T0.AddMinutes(minutes));
        if (access != AccessStatus.Pending) user.Approve(T0);
        if (access == AccessStatus.Suspended) user.Suspend("Kapacita", T0);
        db.Users.Add(user);
        db.SaveChanges();
        if (withProfile)
            ProfileSeeder.Add(db, user.Id, T0);
        return user.Id;
    }

    private async Task<IReadOnlyList<Guid>> ListAsync(bool includeDev = false)
    {
        await using var db = _database.CreateContext();
        return await new ListScoutUsers(db).ExecuteAsync(includeDev);
    }

    private async Task<AnalyzedProfile?> PrepareAsync(Guid userId)
    {
        await using var db = _database.CreateContext();
        return await new PrepareScoutProfile(db, new AnalyzeProfile(db, new ProfileExtractor(_llm), _time)).ExecuteAsync(userId);
    }

    // ---------- who ----------

    [Fact]
    public async Task Only_approved_users_with_a_profile_get_the_morning_run()
    {
        var approved = AddUser("approved");
        AddUser("pending", AccessStatus.Pending);
        AddUser("suspended", AccessStatus.Suspended);                   // costs nothing – that's the point
        AddUser("no-profile", withProfile: false);                    // signed in, never uploaded a CV

        Assert.Equal(new[] { approved }, (await ListAsync()).ToArray());
    }

    [Fact]
    public async Task Dev_personas_run_only_when_asked_for()
    {
        var real = AddUser("real", minutes: 1);
        var persona = AddUser("zednik", provider: SignInUser.DevProvider, minutes: 0);

        Assert.Equal(new[] { real }, (await ListAsync()).ToArray());
        Assert.Equal(new[] { persona, real }, (await ListAsync(includeDev: true)).ToArray());   // oldest first
    }

    // ---------- which profile ----------

    [Fact]
    public async Task Unchanged_cv_and_notes_reuse_the_profile_for_free()
    {
        var userId = AddUser("jana", withProfile: false);
        await using (var db = _database.CreateContext())
            await new AnalyzeProfile(db, new ProfileExtractor(_llm), _time).ExecuteAsync(new ProfileInput(
                "google", "jana", "jana", "cv.md", new LlmDocument("CV", "text/plain", Cv), "Chci full remote."));
        Assert.Equal(1, _llm.Calls);

        var prepared = await PrepareAsync(userId);

        Assert.NotNull(prepared);
        Assert.False(prepared.IsNew);
        Assert.Equal(1, prepared.Version.Version);
        Assert.Equal(1, _llm.Calls);                                  // no new analysis
    }

    [Fact]
    public async Task Changed_prompt_version_re_analyses_the_stored_cv_with_the_same_notes()
    {
        // ProfileSeeder stores a hash that doesn't match the stored CV – exactly what a bumped ProfilePrompts.Version causes
        var userId = AddUser("jana");
        await using (var db = _database.CreateContext())
        {
            db.StoredCvs.Add(new StoredCv(userId, "cv.md", "text/plain", Cv, T0));
            await db.SaveChangesAsync();
        }

        var prepared = await PrepareAsync(userId);

        Assert.NotNull(prepared);
        Assert.True(prepared.IsNew);
        Assert.Equal(2, prepared.Version.Version);
        Assert.Equal(Cv, Assert.Single(Assert.Single(_llm.Requests).Documents).Content);
    }

    [Fact]
    public async Task Profile_from_before_cvs_were_stored_is_used_as_it_is()
    {
        var userId = AddUser("old-timer");                             // profile, but no stored CV

        var prepared = await PrepareAsync(userId);

        Assert.NotNull(prepared);
        Assert.Equal("Testovací profil", prepared.Profile.Headline);
        Assert.Equal(0, _llm.Calls);
    }

    [Fact]
    public async Task User_without_a_profile_is_skipped()
    {
        var userId = AddUser("new", withProfile: false);

        Assert.Null(await PrepareAsync(userId));
    }

    [Fact]
    public async Task Prepared_version_is_tracked_so_the_search_plan_can_be_saved()
    {
        // RunScout stores the search plan on the profile version – it must belong to the same DbContext
        var userId = AddUser("jana");
        await using var db = _database.CreateContext();

        var prepared = await new PrepareScoutProfile(db, new AnalyzeProfile(db, new ProfileExtractor(_llm), _time))
            .ExecuteAsync(userId);

        Assert.Equal(EntityState.Unchanged, db.Entry(prepared!.Version).State);
        Assert.Equal(EntityState.Unchanged, db.Entry(prepared.User).State);
    }
}
