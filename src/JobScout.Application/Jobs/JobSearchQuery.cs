namespace JobScout.Application.Jobs;

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

    /// <summary>Commute radius around Location in km; 0 = the source's default. Ignored without a Location.</summary>
    public int RadiusKm { get; init; }
    public JobAge MaxAge { get; init; } = JobAge.Any;
    public int Page { get; init; } = 1;
}

/// <summary>A category / profession offered by a job source (e.g. jobs.cz "sw-architekt").</summary>
public sealed record JobCategory(string Id, string Label);
