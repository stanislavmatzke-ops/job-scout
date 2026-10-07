using JobScout.Application.Profiles;
using JobScout.Core;

namespace JobScout.Application.Jobs;

/// <summary>Resolved professions × places → searches. Pure function – easy to test and reason about.</summary>
public static class SearchPlanBuilder
{
    /// <summary>Be polite to the portal: at most this many searches per profile per source.</summary>
    public const int MaxSearches = 12;

    public static IReadOnlyList<PlannedSearch> Build(
        SearchIntent intent, WorkPreferences preferences, IReadOnlyList<ResolvedProfession> professions)
    {
        var places = Places(intent, preferences);

        var categories = professions.SelectMany(p => p.Categories).DistinctBy(c => c.Id).ToList();
        var freeText = professions
            .Where(p => p.Categories.Count == 0 && !string.IsNullOrWhiteSpace(p.Profession))
            .Select(p => p.Profession.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Categories first (an order of magnitude better recall), free text only as a fallback
        var what = categories.Select(c => (Category: (string?)c.Id, Keywords: (string?)null))
            .Concat(freeText.Select(k => (Category: (string?)null, Keywords: (string?)k)));

        return what
            .SelectMany(w => places.Select(p => new PlannedSearch(w.Category, w.Keywords, p.Location, p.RadiusKm)))
            .Take(MaxSearches)
            .ToList();
    }

    private static List<(string? Location, int RadiusKm)> Places(SearchIntent intent, WorkPreferences preferences)
    {
        // Accepts full remote → the whole country already contains every place
        if (preferences.AcceptableRemoteWork.Contains(RemoteWork.Full))
            return [(null, 0)];

        var places = new List<(string? Location, int RadiusKm)>();
        if (!string.IsNullOrWhiteSpace(intent.HomeLocation))
            places.Add((intent.HomeLocation.Trim(), Math.Max(0, intent.CommuteRadiusKm)));

        foreach (var extra in intent.ExtraLocations.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim()))
            if (!places.Any(p => string.Equals(p.Location, extra, StringComparison.OrdinalIgnoreCase)))
                places.Add((extra, 0));

        return places.Count > 0 ? places : [(null, 0)];
    }
}