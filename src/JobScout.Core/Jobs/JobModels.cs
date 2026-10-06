namespace JobScout.Core.Jobs;

/// <summary>Search parameters. Location is a source-specific slug (jobs.cz: "praha", "brno").</summary>
public sealed record JobSearchQuery(string Keywords, string? Location = null, int Page = 1);

/// <summary>A job from the search results – a cheap overview without the full text.</summary>
public sealed record JobListing(
    string Source,
    string ExternalId,
    string Title,
    string Company,
    string Location,
    string Url,
    IReadOnlyList<string> Tags);

/// <summary>Full job ad. IsComplete = false when only a fallback was available (e.g. a JS-rendered company page).</summary>
public sealed record JobDetail(
    string Source,
    string ExternalId,
    string Url,
    string Title,
    string Company,
    string Text,
    bool IsComplete);