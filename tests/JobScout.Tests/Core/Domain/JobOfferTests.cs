using JobScout.Core.Domain;
using JobScout.Core.Jobs;

namespace JobScout.Tests.Core.Domain;

/// <summary>Shared job ad: created from a search result, refreshed when seen again, detail added later.</summary>
public class JobOfferTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 6, 8, 0, 0, TimeSpan.Zero);

    private static JobListing Listing(string title = "Senior .NET vývojář", params string[] tags) =>
        new("jobs.cz", "2001426380", title, "Ukázková firma s.r.o.", "Praha – Karlín",
            "https://www.jobs.cz/rpd/2001426380/", tags.Length > 0 ? tags : new[] { "Možnost občasné práce z domova" });

    [Fact]
    public void FromListing_copies_listing_and_sets_seen_times()
    {
        var offer = JobOffer.FromListing(Listing(), T0);

        Assert.NotEqual(Guid.Empty, offer.Id);
        Assert.Equal("jobs.cz", offer.Source);
        Assert.Equal("2001426380", offer.ExternalId);
        Assert.Equal("Senior .NET vývojář", offer.Title);
        Assert.Equal("Ukázková firma s.r.o.", offer.Company);
        Assert.Equal("Praha – Karlín", offer.Location);
        Assert.Equal("https://www.jobs.cz/rpd/2001426380/", offer.Url);
        Assert.Equal(new[] { "Možnost občasné práce z domova" }, offer.Tags);
        Assert.Equal(T0, offer.FirstSeenAt);
        Assert.Equal(T0, offer.LastSeenAt);
        Assert.Null(offer.DetailText);
        Assert.Null(offer.DetailFetchedAt);
    }

    [Fact]
    public void Tags_are_a_copy_not_a_shared_list()
    {
        var tags = new List<string> { "65 000 – 80 000 Kč" };
        var listing = Listing() with { Tags = tags };

        var offer = JobOffer.FromListing(listing, T0);
        tags.Add("added later to the source list");

        Assert.Single(offer.Tags);
    }

    [Fact]
    public void Refresh_updates_data_and_last_seen_but_keeps_identity_and_first_seen()
    {
        // The company "refreshed" the ad and changed the title/salary
        var offer = JobOffer.FromListing(Listing(), T0);
        var id = offer.Id;

        offer.Refresh(Listing("Senior .NET vývojář / Tech Lead", "90 000 – 120 000 Kč"), T0.AddDays(3));

        Assert.Equal(id, offer.Id);
        Assert.Equal(T0, offer.FirstSeenAt);
        Assert.Equal(T0.AddDays(3), offer.LastSeenAt);
        Assert.Equal("Senior .NET vývojář / Tech Lead", offer.Title);
        Assert.Equal(new[] { "90 000 – 120 000 Kč" }, offer.Tags);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]   // e.g. JS career page that couldn't be rendered → snippet only
    public void SetDetail_stores_text_completeness_and_time(bool isComplete)
    {
        var offer = JobOffer.FromListing(Listing(), T0);
        var detail = new JobDetail("jobs.cz", "2001426380", offer.Url, offer.Title, offer.Company,
            "# Senior .NET vývojář\nNáplň práce: …", isComplete);

        offer.SetDetail(detail, T0.AddMinutes(30));

        Assert.Equal("# Senior .NET vývojář\nNáplň práce: …", offer.DetailText);
        Assert.Equal(isComplete, offer.DetailIsComplete);
        Assert.Equal(T0.AddMinutes(30), offer.DetailFetchedAt);
        Assert.Equal(T0, offer.LastSeenAt);   // fetching the detail is not "seen in search results"
    }

    // ---------- added by hand (ADR-0013) ----------

    [Fact]
    public void Ad_added_by_hand_is_private_complete_and_has_its_own_id()
    {
        var owner = Guid.NewGuid();

        var offer = JobOffer.AddedByUser(owner, "Stavbyvedoucí", "Stavby Morava a.s.", "Brno", "", "Hledáme stavbyvedoucího.", T0);

        Assert.True(offer.IsPrivate);
        Assert.Equal(owner, offer.OwnerUserId);
        Assert.Equal((JobOffer.ManualSource, offer.Id.ToString("N")), (offer.Source, offer.ExternalId));
        Assert.Equal(("Hledáme stavbyvedoucího.", true, (DateTimeOffset?)T0), (offer.DetailText, offer.DetailIsComplete, offer.DetailFetchedAt));
        Assert.False(JobOffer.FromListing(Listing(), T0).IsPrivate);
    }

    [Fact]
    public void Ad_added_by_hand_needs_its_text()
    {
        Assert.Throws<ArgumentException>(() => JobOffer.AddedByUser(Guid.NewGuid(), "Pozice", "", "", "", "  ", T0));
    }
}
