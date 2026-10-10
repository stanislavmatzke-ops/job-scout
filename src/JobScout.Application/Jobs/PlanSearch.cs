using JobScout.Application.Abstractions;
using JobScout.Application.Llm;
using JobScout.Application.Profiles;
using JobScout.Core.Domain;

namespace JobScout.Application.Jobs;

public sealed record SearchPlanResult(SearchPlan Plan, bool IsNew, IReadOnlyList<LlmResponse> Calls);

/// <summary>
/// Search intent → per-source plan (ADR-0012, ADR-0015), once per profile version:
/// AI suggests catalogue names → learned catalogue / source verifies → SearchPlanBuilder → stored with the profile.
/// </summary>
public sealed class PlanSearch(
    IAppDbContext db, CategoryResolver resolver, CategorySuggester suggester, IEnumerable<IJobSource> sources)
{
    private const int MaxLabelsPerProfession = 4;

    /// <param name="version">Must be tracked by the same DbContext (same scope) – the plan is saved into it.</param>
    public async Task<SearchPlanResult> ExecuteAsync(UserProfile version, CandidateProfile profile, CancellationToken ct = default)
    {
        if (version.SearchPlanJson is not null)
        {
            var stored = LlmJson.Deserialize<SearchPlan>(version.SearchPlanJson);
            if (stored.Version == SearchPlan.CurrentVersion)
                return new SearchPlanResult(stored, IsNew: false, Calls: []);
        }

        var professions = profile.Search.Professions
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var calls = new List<LlmResponse>();
        var plans = new Dictionary<string, IReadOnlyList<PlannedSearch>>();

        foreach (var source in sources)
        {
            var resolved = new List<ResolvedProfession>();
            if (professions.Count > 0)
            {
                var known = await resolver.KnownAsync(source, ct);
                var suggestion = await suggester.SuggestAsync(source.Name, professions, known, ct);
                calls.Add(suggestion.Response);

                foreach (var profession in professions)
                    resolved.Add(new ResolvedProfession(profession, await VerifyAsync(source, profession, suggestion.Suggestions, ct)));
            }

            plans[source.Name] = SearchPlanBuilder.Build(profile.Search, profile.Preferences, resolved);
        }

        var plan = new SearchPlan(SearchPlan.CurrentVersion, plans);
        version.SetSearchPlan(LlmJson.Serialize(plan));
        await db.SaveChangesAsync(ct);

        return new SearchPlanResult(plan, IsNew: true, calls);
    }

    private async Task<IReadOnlyList<JobCategory>> VerifyAsync(
        IJobSource source, string profession, CategorySuggestions suggestions, CancellationToken ct)
    {
        var labels = suggestions.Mappings
            .Where(m => string.Equals(m.Profession.Trim(), profession, StringComparison.OrdinalIgnoreCase))
            .SelectMany(m => m.Labels)
            .Append(profession)                       // the name itself is always worth a check
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxLabelsPerProfession);

        var categories = new List<JobCategory>();
        foreach (var label in labels)
            if (await resolver.ResolveAsync(source, label, ct) is { } category && categories.All(c => c.Id != category.Id))
                categories.Add(category);

        return categories;
    }
}