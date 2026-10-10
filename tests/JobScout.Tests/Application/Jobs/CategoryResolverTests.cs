using JobScout.Application.Jobs;
using JobScout.Core.Domain;
using JobScout.Tests.TestUtils;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Tests.Application.Jobs;

/// <summary>
/// Learned catalogue, cache-aside (ADR-0015): ask the table first, the source only when unknown or stale,
/// and remember the answer – positive and negative – for everyone.
/// </summary>
public sealed class CategoryResolverTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 7, 8, 0, 0, TimeSpan.Zero);

    private readonly SqliteTestDatabase _database = new();
    private readonly FixedTimeProvider _time = new(T0);
    private readonly FakeJobSource _source = new("jobs.cz");

    public CategoryResolverTests()
    {
        _source.Existing["Lakýrník"] = "lakyrnik";
        _source.Existing["Zedník"] = "zednik";
    }

    public void Dispose() => _database.Dispose();

    /// <summary>Fresh DbContext per call – like separate runs / users.</summary>
    private async Task<JobCategory?> ResolveAsync(string label, IJobSource? source = null)
    {
        await using var db = _database.CreateContext();
        return await new CategoryResolver(db, _time).ResolveAsync(source ?? _source, label);
    }

    private async Task<IReadOnlyList<JobCategory>> KnownAsync(IJobSource? source = null)
    {
        await using var db = _database.CreateContext();
        return await new CategoryResolver(db, _time).KnownAsync(source ?? _source);
    }

    private async Task<List<SourceCategory>> RowsAsync()
    {
        await using var db = _database.CreateContext();
        return await db.SourceCategories.ToListAsync();
    }

    // ---------- First time: ask the source and remember ----------

    [Fact]
    public async Task Unknown_profession_is_asked_once_and_stored()
    {
        var category = await ResolveAsync("Lakýrník");

        Assert.Equal("lakyrnik", category?.Id);
        Assert.Single(_source.ResolveRequests);
        var row = Assert.Single(await RowsAsync());
        Assert.Equal(("jobs.cz", "lakyrnik", "lakyrnik", T0), (row.Source, row.Key, row.CategoryId, row.CheckedAt));
    }

    [Fact]
    public async Task Missing_profession_is_stored_as_negative()
    {
        var category = await ResolveAsync("Autolakýrník");

        Assert.Null(category);
        var row = Assert.Single(await RowsAsync());
        Assert.False(row.Exists);
    }

    // ---------- Next time: from the table, no request ----------

    [Fact]
    public async Task Known_profession_is_answered_from_the_table()
    {
        await ResolveAsync("Lakýrník");
        var second = await ResolveAsync("Lakýrník");

        Assert.Equal("lakyrnik", second?.Id);
        Assert.Single(_source.ResolveRequests);       // the whole point: no second request
    }

    [Fact]
    public async Task Known_negative_answer_is_not_asked_again()
    {
        await ResolveAsync("Autolakýrník");
        var second = await ResolveAsync("Autolakýrník");

        Assert.Null(second);
        Assert.Single(_source.ResolveRequests);
    }

    [Theory]
    [InlineData("lakýrník")]
    [InlineData("  LAKÝRNÍK ")]
    [InlineData("Lakyrnik")]
    public async Task Different_spelling_of_the_same_name_hits_the_same_row(string spelling)
    {
        await ResolveAsync("Lakýrník");
        var again = await ResolveAsync(spelling);

        Assert.Equal("lakyrnik", again?.Id);
        Assert.Single(_source.ResolveRequests);
        Assert.Single(await RowsAsync());
    }

    [Fact]
    public async Task Same_profession_on_another_source_is_asked_separately()
    {
        var other = new FakeJobSource("prace.cz");
        other.Existing["Lakýrník"] = "lakyrnik-autolakyrnik";

        await ResolveAsync("Lakýrník");
        var onOther = await ResolveAsync("Lakýrník", other);

        Assert.Equal("lakyrnik-autolakyrnik", onOther?.Id);
        Assert.Single(other.ResolveRequests);
        Assert.Equal(2, (await RowsAsync()).Count);
    }

    // ---------- Staleness ----------

    [Fact]
    public async Task Stale_entry_is_rechecked_and_updated()
    {
        await ResolveAsync("Zedník");
        _source.Existing.Remove("Zedník");                     // the portal dropped the profession
        _time.Now = T0 + CategoryResolver.MaxAge + TimeSpan.FromDays(1);

        var again = await ResolveAsync("Zedník");

        Assert.Null(again);
        Assert.Equal(2, _source.ResolveRequests.Count);
        var row = Assert.Single(await RowsAsync());             // updated, not duplicated
        Assert.False(row.Exists);
        Assert.Equal(_time.Now, row.CheckedAt);
    }

    [Fact]
    public async Task Fresh_entry_is_not_rechecked()
    {
        await ResolveAsync("Zedník");
        _time.Now = T0 + CategoryResolver.MaxAge - TimeSpan.FromDays(1);

        await ResolveAsync("Zedník");

        Assert.Single(_source.ResolveRequests);
    }

    // ---------- Failures ----------

    [Fact]
    public async Task Source_outage_stores_nothing()
    {
        // 5xx is not "doesn't exist" – a wrong negative would stick for 90 days
        _source.FailWith = new HttpRequestException("503");

        await Assert.ThrowsAsync<HttpRequestException>(() => ResolveAsync("Lakýrník"));

        Assert.Empty(await RowsAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Empty_label_resolves_to_null_without_asking(string label)
    {
        Assert.Null(await ResolveAsync(label));
        Assert.Empty(_source.ResolveRequests);
        Assert.Empty(await RowsAsync());
    }

    // ---------- Known categories for the AI ----------

    [Fact]
    public async Task Known_lists_verified_categories_and_the_source_catalogue_without_negatives()
    {
        var source = new FakeJobSource("jobs.cz") { Categories = [new("sw-architekt", "SW architekt")] };
        source.Existing["Lakýrník"] = "lakyrnik";

        await ResolveAsync("Lakýrník", source);
        await ResolveAsync("Autolakýrník", source);   // negative – must not be offered to the AI

        var known = await KnownAsync(source);

        Assert.Equal(new[] { "lakyrnik", "sw-architekt" }, known.Select(c => c.Id).Order().ToArray());
    }

    [Fact]
    public async Task Known_does_not_mix_sources()
    {
        await ResolveAsync("Lakýrník");                          // jobs.cz

        var known = await KnownAsync(new FakeJobSource("prace.cz"));

        Assert.Empty(known);
    }
}
