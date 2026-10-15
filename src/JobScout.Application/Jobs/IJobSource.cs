using JobScout.Core.Jobs;
namespace JobScout.Application.Jobs;

public interface IJobSource
{
    string Name { get; }

    /// <summary>Categories / professions this source can search by.</summary>
    IReadOnlyList<JobCategory> Categories { get; }

    /// <summary>Searches up to <paramref name="maxPages"/> result pages; duplicates across pages removed.</summary>
    Task<IReadOnlyList<JobListing>> SearchAsync(JobSearchQuery query, int maxPages = 1, CancellationToken ct = default);

    Task<JobDetail> GetDetailAsync(string externalId, CancellationToken ct = default);

    /// <summary>
    /// A link the user pasted ("add a job by hand", ADR-0013) → this source's id of the ad, or null when the link is not
    /// an ad of this source. Such an ad is then fetched as a normal public one, not scraped as an unknown page.
    /// </summary>
    string? ExternalIdFromUrl(Uri url);
    
    /// <summary>
    /// Turns a profession name into this source's category, if the source has one (ADR-0015).
    /// Returns null when the source doesn't know it – the caller falls back to free text.
    /// </summary>
    Task<JobCategory?> ResolveCategoryAsync(string label, CancellationToken ct = default);
}