namespace JobScout.Core.Jobs;

/// <summary>How old the job ads may be.</summary>
public enum JobAge { Any, Day, ThreeDays }

/// <summary>
/// Search parameters. All optional – combine as needed.
/// Category = id from IJobSource.Categories; Location = place name as people write it ("Praha"), null = whole country.
/// </summary>
public sealed record JobSearchQuery
{
    public string? Category { get; init; }
    public string? Keywords { get; init; }
    public string? Location { get; init; }
    public JobAge MaxAge { get; init; } = JobAge.Any;
    public int Page { get; init; } = 1;
}

/// <summary>A category / profession offered by a job source (e.g. jobs.cz "sw-architekt").</summary>
public sealed record JobCategory(string Id, string Label);

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