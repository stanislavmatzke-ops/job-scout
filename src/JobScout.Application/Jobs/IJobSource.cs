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
}