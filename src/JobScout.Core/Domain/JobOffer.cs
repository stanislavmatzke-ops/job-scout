using JobScout.Core.Jobs;

namespace JobScout.Core.Domain;

/// <summary>
/// A job ad from a source – shared by all users. Unique by Source + ExternalId.
/// Exception: an ad the user added by hand (pasted text, a file, an e-mail, a web page) is private to them (ADR-0013).
/// </summary>
public sealed class JobOffer
{
    /// <summary>Source of the ads users add by hand; their ExternalId is the offer's own id.</summary>
    public const string ManualSource = "manual";

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
    /// <summary>null = a public ad from a source (shared); set = a private ad only this user may see (ADR-0013).</summary>
    public Guid? OwnerUserId { get; private set; }

    public bool IsPrivate => OwnerUserId is not null;

    private JobOffer() { }
    
    public static JobOffer FromListing(JobListing listing, DateTimeOffset now) => new JobOffer
    {
        Id = Guid.CreateVersion7(now),
        Source = listing.Source,
        ExternalId = listing.ExternalId,
        FirstSeenAt = now,
    }.Apply(listing, now);
    
    /// <summary>
    /// An ad the user brought from elsewhere – already as text (extracted by the AI from a PDF, an e-mail, a web page).
    /// Nothing to fetch later, so the detail is set right away and is complete.
    /// </summary>
    /// <param name="url">The page it came from, or "" (pasted text, a file).</param>
    public static JobOffer AddedByUser(
        Guid ownerUserId, string title, string company, string location, string url, string text, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("A manually added ad needs its text.", nameof(text));

        var id = Guid.CreateVersion7(now);
        var offer = new JobOffer
        {
            Id = id,
            Source = ManualSource,
            ExternalId = id.ToString("N"),
            OwnerUserId = ownerUserId,
            FirstSeenAt = now,
        }.Apply(new JobListing(ManualSource, id.ToString("N"), title, company, location, url, []), now);
        offer.SetDetail(new JobDetail(ManualSource, offer.ExternalId, url, title, company, text, IsComplete: true), now);
        return offer;
    }

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