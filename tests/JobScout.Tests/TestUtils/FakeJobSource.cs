using JobScout.Application.Jobs;
using JobScout.Core.Jobs;

namespace JobScout.Tests.TestUtils;

/// <summary>
/// Fake job source: knows a fixed set of professions and counts how often it was asked –
/// so tests can prove that the learned catalogue saves requests.
/// </summary>
internal sealed class FakeJobSource(string name = "fake.cz") : IJobSource
{
    /// <summary>Profession label → category id the "portal" has. Anything else = 404.</summary>
    public Dictionary<string, string> Existing { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> ResolveRequests { get; } = [];

    /// <summary>ExternalId → detail the "portal" returns. Missing id = 404 (ad removed).</summary>
    public Dictionary<string, JobDetail> Details { get; } = [];

    /// <summary>ExternalId → exception thrown when its detail is requested (e.g. 500, timeout).</summary>
    public Dictionary<string, Exception> DetailFailures { get; } = [];

    public List<string> DetailRequests { get; } = [];
    public Exception? FailWith { get; set; }

    public string Name => name;
    public IReadOnlyList<JobCategory> Categories { get; init; } = [];

    public Task<JobCategory?> ResolveCategoryAsync(string label, CancellationToken ct = default)
    {
        ResolveRequests.Add(label);
        if (FailWith is not null)
            throw FailWith;

        return Task.FromResult(Existing.TryGetValue(label.Trim(), out var id)
            ? new JobCategory(id, label.Trim())
            : null);
    }

    /// <summary>What every search returns (the fake portal ignores the query).</summary>
    public List<JobListing> Listings { get; } = [];

    public List<JobSearchQuery> SearchQueries { get; } = [];

    /// <summary>When set, every search throws it (portal down).</summary>
    public Exception? SearchFailure { get; set; }

    public Task<IReadOnlyList<JobListing>> SearchAsync(JobSearchQuery query, int maxPages = 1, CancellationToken ct = default)
    {
        SearchQueries.Add(query);
        if (SearchFailure is not null)
            throw SearchFailure;

        return Task.FromResult<IReadOnlyList<JobListing>>(Listings.ToList());
    }

    /// <summary>https://fake.cz/job/{id} is an ad of this source.</summary>
    public string? ExternalIdFromUrl(Uri url) =>
        url.Host == name && url.AbsolutePath.StartsWith("/job/", StringComparison.Ordinal)
            ? url.AbsolutePath["/job/".Length..].Trim('/')
            : null;

    public Task<JobDetail> GetDetailAsync(string externalId, CancellationToken ct = default)
    {
        DetailRequests.Add(externalId);
        if (DetailFailures.TryGetValue(externalId, out var failure))
            throw failure;

        return Details.TryGetValue(externalId, out var detail)
            ? Task.FromResult(detail)
            : throw new HttpRequestException("Not Found", null, System.Net.HttpStatusCode.NotFound);
    }
}
