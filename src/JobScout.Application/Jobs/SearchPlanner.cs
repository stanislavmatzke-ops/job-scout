using JobScout.Application.Profiles;
using JobScout.Core;

namespace JobScout.Application.Jobs;

/// <summary>Profile → searches for the morning run. Pure logic: no AI, no network.</summary>
public static class SearchPlanner
{
    /// <summary>Be gentle to the job portal: at most this many search requests per profile per run.</summary>
    public const int MaxQueries = 8;

    private static readonly string[] RemoteWords = ["remote", "home office", "z domova", "na dálku", "kdekoli"];

    public static IReadOnlyList<JobSearch.JobSearchQuery> Plan(CandidateProfile profile)
    {
        var prefs = profile.Preferences;

        // Real places only ("remote" is not a city)
        var places = prefs.Locations
            .Where(l => !string.IsNullOrWhiteSpace(l) && !IsRemote(l))
            .Select(l => l.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string?>()
            .ToList();

        // Accepts full remote, or no city given → also search the whole country (location = null)
        if (places.Count == 0 || prefs.AcceptableRemoteWork.Contains(RemoteWork.Full))
            places.Add(null);

        return profile.SearchKeywords
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(keyword => places.Select(place => new JobSearch.JobSearchQuery { Keywords = keyword, Location = place }))
            .Take(MaxQueries)
            .ToList();
    }

    private static bool IsRemote(string location) =>
        RemoteWords.Any(w => location.Contains(w, StringComparison.OrdinalIgnoreCase));
}