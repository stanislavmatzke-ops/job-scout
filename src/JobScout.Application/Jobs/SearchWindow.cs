namespace JobScout.Application.Jobs;

/// <summary>How far back to search, derived from the last SUCCESSFUL run. HasGap = some ads may have been missed.</summary>
public sealed record SearchWindow(JobSearch.JobAge MaxAge, bool HasGap)
{
    public static SearchWindow For(DateTimeOffset? lastSuccessStartedAt, DateTimeOffset now)
    {
        if (lastSuccessStartedAt is null)
            return new(JobSearch.JobAge.ThreeDays, HasGap: false);          // first run

        var elapsed = now - lastSuccessStartedAt.Value;
        if (elapsed <= TimeSpan.FromHours(24))
            return new(JobSearch.JobAge.Day, HasGap: false);                // the normal morning

        // Missed a morning (or ran earlier yesterday) – 3 days covers it; beyond that ads may be lost
        return new(JobSearch.JobAge.ThreeDays, HasGap: elapsed > TimeSpan.FromDays(3));
    }
}