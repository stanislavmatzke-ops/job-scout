namespace JobScout.Application.Jobs;

/// <summary>One planned search on one source; the morning run adds the age filter.</summary>
public sealed record PlannedSearch(string? Category, string? Keywords, string? Location, int RadiusKm)
{
    public JobSearchQuery ToQuery(JobAge maxAge) => new()
    {
        Category = Category, Keywords = Keywords, Location = Location, RadiusKm = RadiusKm, MaxAge = maxAge,
    };
}

/// <summary>Searches per source (key = IJobSource.Name), stored as JSON with the profile version.</summary>
public sealed record SearchPlan(int Version, IReadOnlyDictionary<string, IReadOnlyList<PlannedSearch>> Sources)
{
    /// <summary>Bump when the planning logic changes – stored plans are then rebuilt on the next run.</summary>
    public const int CurrentVersion = 3;

    public IReadOnlyList<PlannedSearch> For(string source) =>
        Sources.TryGetValue(source, out var searches) ? searches : [];
}

/// <summary>A candidate's profession and the categories a source verified for it (empty = free text).</summary>
public sealed record ResolvedProfession(string Profession, IReadOnlyList<JobCategory> Categories);