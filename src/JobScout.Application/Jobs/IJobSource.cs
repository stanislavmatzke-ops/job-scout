using JobScout.Core.Jobs;
namespace JobScout.Application.Jobs;

public interface IJobSource
{
    string Name { get; }

    /// <summary>Categories / professions this source can search by.</summary>
    IReadOnlyList<JobSearch.JobCategory> Categories { get; }

    /// <summary>Searches up to <paramref name="maxPages"/> result pages; duplicates across pages removed.</summary>
    Task<IReadOnlyList<JobListing>> SearchAsync(JobSearch.JobSearchQuery query, int maxPages = 1, CancellationToken ct = default);

    Task<JobDetail> GetDetailAsync(string externalId, CancellationToken ct = default);
    
    /// <summary>
    /// Turns a profession name into this source's category, if the source has one (ADR-0015).
    /// Returns null when the source doesn't know it – the caller falls back to free text.
    /// </summary>
    Task<JobSearch.JobCategory?> ResolveCategoryAsync(string label, CancellationToken ct = default);
}