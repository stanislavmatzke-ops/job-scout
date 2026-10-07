namespace JobScout.Core.Jobs;

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