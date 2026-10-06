using JobScout.Core.Jobs;

namespace JobScout.Core.Domain;

/// <summary>A job ad from a source – shared by all users. Unique by Source + ExternalId.</summary>
public sealed class JobOffer
{
    public Guid Id { get; private set; }
    public string Source { get; private set; } = "";
    public string ExternalId { get; private set; } = "";
    public string Url { get; private set; } = "";
    public string Title { get; private set; } = "";
    public string Company { get; private set; } = "";
    public string Location { get; private set; } = "";
    public List<string> Tags { get; private set; } = [];
    public string? DetailText { get; private set; }
    public bool DetailIsComplete { get; private set; }
    public DateTimeOffset? DetailFetchedAt { get; private set; }
    public DateTimeOffset FirstSeenAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }

    private JobOffer() { }
    
    public static JobOffer FromListing(JobListing listing, DateTimeOffset now) => new JobOffer
    {
        Id = Guid.CreateVersion7(now),
        Source = listing.Source,
        ExternalId = listing.ExternalId,
        FirstSeenAt = now,
    }.Apply(listing, now);
    
    /// <summary>Seen again in search results (also when the company just "refreshed" the ad).</summary>
    public void Refresh(JobListing listing, DateTimeOffset now) => Apply(listing, now);

    public void SetDetail(JobDetail detail, DateTimeOffset now)
    {
        DetailText = detail.Text;
        DetailIsComplete = detail.IsComplete;
        DetailFetchedAt = now;
    }

    private JobOffer Apply(JobListing listing, DateTimeOffset now)
    {
        Url = listing.Url;
        Title = listing.Title;
        Company = listing.Company;
        Location = listing.Location;
        Tags = [.. listing.Tags];
        LastSeenAt = now;
        return this;
    }
}