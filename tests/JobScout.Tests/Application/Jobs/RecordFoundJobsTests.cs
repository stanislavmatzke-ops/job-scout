using JobScout.Application.Jobs;
using JobScout.Core.Domain;
using JobScout.Core.Jobs;
using JobScout.Tests.TestUtils;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Tests.Application.Jobs;

/// <summary>
/// "Morning run shows only new jobs": shared JobOffer per ad, JobMatch per user.
/// An ad the user has already seen – in any stage or status, including Hidden – never comes back.
/// </summary>
public sealed class RecordFoundJobsTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 7, 6, 0, 0, TimeSpan.Zero);

    private readonly SqliteTestDatabase _database = new();
    private readonly FixedTimeProvider _time = new(T0);
    private readonly Guid _painter;
    private readonly Guid _bricklayer;

    public RecordFoundJobsTests()
    {
        using var db = _database.CreateContext();
        var painter = new User("dev", "autolakyrnik", "", "autolakyrnik", T0);
        var bricklayer = new User("dev", "zednik", "", "zednik", T0);
        db.AddRange(painter, bricklayer);
        db.SaveChanges();
        (_painter, _bricklayer) = (painter.Id, bricklayer.Id);
    }

    public void Dispose() => _database.Dispose();

    private static JobListing Listing(string id, string title = "Lakýrník", string source = "jobs.cz") =>
        new(source, id, title, "Ukázková firma", "Kolín", $"https://www.jobs.cz/rpd/{id}/", ["Plný úvazek"]);

    private async Task<RecordFoundJobsResult> RecordAsync(Guid userId, params JobListing[] listings)
    {
        await using var db = _database.CreateContext();
        return await new RecordFoundJobs(db, _time).ExecuteAsync(userId, profileVersion: 2, listings);
    }

    private long Count(string table) => (long)_database.Scalar($"SELECT COUNT(*) FROM \"{table}\"")!;

    // ---------- First run ----------

    [Fact]
    public async Task First_run_stores_offers_and_creates_new_matches()
    {
        var result = await RecordAsync(_painter, Listing("2001000001"), Listing("2001000002"));

        Assert.Equal(2, result.Found);
        Assert.Equal(2, result.NewForUser.Count);
        Assert.Equal(2, result.NewOffers);
        Assert.Equal(0, result.AlreadySeen);
        Assert.Equal(2, Count("JobOffers"));
        Assert.Equal(2, Count("JobMatches"));
    }

    [Fact]
    public async Task New_match_starts_as_found_new_with_profile_version()
    {
        var result = await RecordAsync(_painter, Listing("2001000001"));

        var job = Assert.Single(result.NewForUser);
        Assert.Equal(MatchStage.Found, job.Match.Stage);
        Assert.Equal(MatchStatus.New, job.Match.Status);
        Assert.Equal(2, job.Match.ProfileVersion);
        Assert.Equal(_painter, job.Match.UserId);
        Assert.Equal(job.Offer.Id, job.Match.JobOfferId);
        Assert.Equal("Lakýrník", job.Offer.Title);
    }

    // ---------- Next runs: only new ----------

    [Fact]
    public async Task Second_run_with_the_same_ads_shows_nothing_new()
    {
        await RecordAsync(_painter, Listing("2001000001"), Listing("2001000002"));
        var second = await RecordAsync(_painter, Listing("2001000001"), Listing("2001000002"));

        Assert.Empty(second.NewForUser);
        Assert.Equal(2, second.AlreadySeen);
        Assert.Equal(0, second.NewOffers);
        Assert.Equal(2, Count("JobMatches"));       // no duplicates
    }

    [Fact]
    public async Task Only_the_really_new_ad_is_returned()
    {
        await RecordAsync(_painter, Listing("2001000001"));
        var second = await RecordAsync(_painter, Listing("2001000001"), Listing("2001000003"));

        Assert.Equal("2001000003", Assert.Single(second.NewForUser).Offer.ExternalId);
    }

    [Fact]
    public async Task Refreshed_ad_updates_the_offer_but_is_not_offered_again()
    {
        // Companies "refresh" ads – same ID, maybe a new title; the user has seen it already
        await RecordAsync(_painter, Listing("2001000001", "Lakýrník"));
        _time.Now = T0.AddDays(1);
        var second = await RecordAsync(_painter, Listing("2001000001", "Lakýrník – nově i 4denní týden"));

        Assert.Empty(second.NewForUser);
        await using var db = _database.CreateContext();
        var offer = await db.JobOffers.SingleAsync();
        Assert.Equal("Lakýrník – nově i 4denní týden", offer.Title);
        Assert.Equal(T0, offer.FirstSeenAt);
        Assert.Equal(T0.AddDays(1), offer.LastSeenAt);
    }

    [Theory]
    [InlineData(MatchStatus.Hidden)]       // "Nechci vidět" – never offer again
    [InlineData(MatchStatus.Dismissed)]
    [InlineData(MatchStatus.Applied)]
    public async Task Ad_seen_in_any_status_never_comes_back(MatchStatus status)
    {
        await RecordAsync(_painter, Listing("2001000001"));
        await using (var db = _database.CreateContext())
        {
            var match = await db.JobMatches.SingleAsync();
            match.ChangeStatus(status, null, T0.AddHours(1));
            await db.SaveChangesAsync();
        }

        var again = await RecordAsync(_painter, Listing("2001000001"));

        Assert.Empty(again.NewForUser);
    }

    [Fact]
    public async Task Ad_triaged_out_earlier_does_not_come_back()
    {
        await RecordAsync(_painter, Listing("2001000001"));
        await using (var db = _database.CreateContext())
        {
            (await db.JobMatches.SingleAsync()).MarkTriagedOut("Jiný obor", T0);
            await db.SaveChangesAsync();
        }

        Assert.Empty((await RecordAsync(_painter, Listing("2001000001"))).NewForUser);
    }

    // ---------- Shared offers, personal matches ----------

    [Fact]
    public async Task Same_ad_for_another_user_reuses_the_offer_and_creates_his_match()
    {
        await RecordAsync(_painter, Listing("2001000001"));
        var other = await RecordAsync(_bricklayer, Listing("2001000001"));

        Assert.Single(other.NewForUser);
        Assert.Equal(0, other.NewOffers);          // the ad is stored once for everyone
        Assert.Equal(1, Count("JobOffers"));
        Assert.Equal(2, Count("JobMatches"));
    }

    [Fact]
    public async Task Same_id_on_another_source_is_another_offer()
    {
        var result = await RecordAsync(_painter, Listing("123", source: "jobs.cz"), Listing("123", source: "prace.cz"));

        Assert.Equal(2, result.NewForUser.Count);
        Assert.Equal(2, Count("JobOffers"));
    }

    // ---------- Input hygiene ----------

    [Fact]
    public async Task Duplicates_in_the_input_are_recorded_once()
    {
        // The same ad found by two searches (e.g. two professions) in one run
        var result = await RecordAsync(_painter, Listing("2001000001"), Listing("2001000001"));

        Assert.Equal(1, result.Found);
        Assert.Single(result.NewForUser);
        Assert.Equal(1, Count("JobMatches"));
    }

    [Fact]
    public async Task Empty_input_does_nothing()
    {
        var result = await RecordAsync(_painter);

        Assert.Equal(0, result.Found);
        Assert.Empty(result.NewForUser);
        Assert.Equal(0, Count("JobOffers"));
    }
}
