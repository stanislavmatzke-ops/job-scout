namespace JobScout.Core.Jobs;

public interface IJobSource
{
    string Name { get; }
    Task<IReadOnlyList<JobListing>> SearchAsync(JobSearchQuery query, CancellationToken ct = default);
    Task<JobDetail> GetDetailAsync(string externalId, CancellationToken ct = default);
}