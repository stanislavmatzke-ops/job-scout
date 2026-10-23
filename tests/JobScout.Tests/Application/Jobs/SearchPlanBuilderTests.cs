using JobScout.Application.Jobs;
using JobScout.Core;
using JobScout.Tests.TestUtils;

namespace JobScout.Tests.Application.Jobs;

/// <summary>Pure planning logic: resolved professions × places → searches. No AI, no network.</summary>
public class SearchPlanBuilderTests
{
    private static readonly JobCategory Lakyrnik = new("lakyrnik", "Lakýrník");
    private static readonly JobCategory Pripravar = new("pripravar", "Přípravář");

    private static IReadOnlyList<PlannedSearch> Build(
        IReadOnlyList<ResolvedProfession> resolved,
        string home = "Kolín", int radius = 30,
        IReadOnlyList<string>? extra = null, IReadOnlyList<RemoteWork>? acceptableRemote = null)
    {
        var profile = TestProfiles.With(resolved.Select(r => r.Profession).ToList(), home, radius, extra, acceptableRemote);
        return SearchPlanBuilder.Build(profile.Search, profile.Preferences, resolved);
    }

    [Fact]
    public void Category_is_searched_around_home_with_commute_radius()
    {
        var plan = Build([new("Autolakýrník", [Lakyrnik])]);

        Assert.Equal(new PlannedSearch("lakyrnik", null, "Kolín", 30), plan[0]);
    }

    [Fact]
    public void The_job_title_is_also_searched_as_text_after_its_category()
    {
        // Employers file the same job under different categories – the text search catches those (22 Oct 2026)
        var plan = Build([new("Autolakýrník", [Lakyrnik])]);

        Assert.Equal(
            new[] { new PlannedSearch("lakyrnik", null, "Kolín", 30), new PlannedSearch(null, "Autolakýrník", "Kolín", 30) },
            plan.ToArray());
    }

    [Fact]
    public void Only_the_most_important_titles_are_searched_as_text()
    {
        var professions = Enumerable.Range(1, 5)
            .Select(i => new ResolvedProfession($"Profese {i}", [new JobCategory($"profese-{i}", $"Profese {i}")]))
            .ToList();

        var plan = Build(professions);

        Assert.Equal(new string?[] { "Profese 1", "Profese 2", "Profese 3" },
            plan.Where(s => s.Category is null).Select(s => s.Keywords).ToArray());
    }

    [Fact]
    public void When_the_cap_is_reached_the_title_texts_are_left_out_not_the_categories()
    {
        var many = Enumerable.Range(1, 6)
            .Select(i => new ResolvedProfession($"Profese {i}", [new JobCategory($"profese-{i}", $"Profese {i}")]))
            .ToList();

        var plan = Build(many, extra: ["Praha"]);   // 6 categories × 2 places = 12 = the cap

        Assert.Equal(SearchPlanBuilder.MaxSearches, plan.Count);
        Assert.All(plan, s => Assert.NotNull(s.Category));
    }

    [Fact]
    public void Profession_without_category_falls_back_to_free_text()
    {
        // Nothing may silently disappear (ADR-0015)
        var plan = Build([new("Smart repair technik", [])]);

        Assert.Equal(new PlannedSearch(null, "Smart repair technik", "Kolín", 30), Assert.Single(plan));
    }

    [Fact]
    public void Same_category_from_two_professions_is_searched_once()
    {
        var plan = Build([new("Autolakýrník", [Lakyrnik]), new("Lakýrník automobilů", [Lakyrnik])]);

        Assert.Single(plan, s => s.Category == "lakyrnik");
    }

    [Fact]
    public void Categories_come_before_free_text()
    {
        var plan = Build([new("Smart repair technik", []), new("Autolakýrník", [Lakyrnik, Pripravar])]);

        Assert.Equal(new[] { "lakyrnik", "pripravar", null, null }, plan.Select(s => s.Category).ToArray());
        Assert.Equal(new string?[] { "Smart repair technik", "Autolakýrník" }, plan.Skip(2).Select(s => s.Keywords).ToArray());
    }

    [Fact]
    public void Extra_locations_are_searched_without_radius()
    {
        var plan = Build([new("Autolakýrník", [Lakyrnik])], extra: ["Praha", " kolín "]);   // home repeated → ignored

        Assert.Equal(
            new[] { new PlannedSearch("lakyrnik", null, "Kolín", 30), new PlannedSearch("lakyrnik", null, "Praha", 0) },
            plan.Where(s => s.Category is not null).ToArray());
    }

    [Fact]
    public void Full_remote_acceptable_searches_the_whole_country_only()
    {
        // The whole country already contains the home town – searching both would just duplicate
        var plan = Build([new("SW architekt", [new("sw-architekt", "SW architekt")])],
            home: "Praha", radius: 0, extra: ["Brno"], acceptableRemote: [RemoteWork.Full, RemoteWork.Hybrid]);

        Assert.Equal(
            new[] { new PlannedSearch("sw-architekt", null, null, 0), new PlannedSearch(null, "SW architekt", null, 0) },
            plan.ToArray());
    }

    [Fact]
    public void No_location_at_all_searches_the_whole_country()
    {
        var plan = Build([new("Zedník", [new("zednik", "Zedník")])], home: "", radius: 0);

        Assert.Equal(new PlannedSearch("zednik", null, null, 0), plan[0]);
        Assert.All(plan, s => Assert.Null(s.Location));
    }

    [Fact]
    public void Plan_is_capped_to_stay_polite()
    {
        var many = Enumerable.Range(1, 10)
            .Select(i => new ResolvedProfession($"Profese {i}", [new JobCategory($"profese-{i}", $"Profese {i}")]))
            .ToList();

        var plan = Build(many, extra: ["Praha"]);   // 10 categories × 2 places = 20

        Assert.Equal(SearchPlanBuilder.MaxSearches, plan.Count);
    }

    [Fact]
    public void Empty_professions_give_an_empty_plan()
    {
        Assert.Empty(Build([]));
    }

    [Fact]
    public void Planned_search_becomes_a_query_with_age_filter()
    {
        var query = new PlannedSearch("lakyrnik", null, "Kolín", 30).ToQuery(JobAge.Day);

        Assert.Equal("lakyrnik", query.Category);
        Assert.Equal("Kolín", query.Location);
        Assert.Equal(30, query.RadiusKm);
        Assert.Equal(JobAge.Day, query.MaxAge);
        Assert.Equal(1, query.Page);
    }

    // ---------- the whole field first (plan v5) ----------

    [Fact]
    public void The_whole_field_is_searched_first_then_the_professions()
    {
        var field = new JobCategory("automobilovy-prumysl", "Automobilový průmysl");
        var profile = TestProfiles.With(["Autolakýrník"], "Kolín", 30, null, null);

        var plan = SearchPlanBuilder.Build(profile.Search, profile.Preferences, [new("Autolakýrník", [Lakyrnik])], field);

        Assert.Equal(new[] { "automobilovy-prumysl", "lakyrnik", null }, plan.Select(s => s.Category).ToArray());
        Assert.All(plan, s => Assert.Equal(("Kolín", 30), (s.Location, s.RadiusKm)));     // the same places as everything else
    }

    [Fact]
    public void A_field_that_is_also_a_profession_category_is_searched_once()
    {
        var field = new JobCategory("lakyrnik", "Lakýrník");
        var profile = TestProfiles.With(["Autolakýrník"], "Kolín", 30, null, null);

        var plan = SearchPlanBuilder.Build(profile.Search, profile.Preferences, [new("Autolakýrník", [Lakyrnik])], field);

        Assert.Single(plan, s => s.Category == "lakyrnik");
    }
}
