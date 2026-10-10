using JobScout.Application.Abstractions;
using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Jobs;

/// <summary>
/// Profession name → source category via the learned catalogue (ADR-0015), cache-aside:
/// the table first; the source only when the name is unknown or the answer is stale; then remember it.
/// </summary>
public sealed class CategoryResolver(IAppDbContext db, TimeProvider time)
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(90);

    public async Task<JobCategory?> ResolveAsync(IJobSource source, string label, CancellationToken ct = default)
    {
        var key = SourceCategory.NormalizeKey(label);
        if (key.Length == 0)
            return null;

        var now = time.GetUtcNow();
        var known = await db.SourceCategories.SingleOrDefaultAsync(c => c.Source == source.Name && c.Key == key, ct);

        if (known is not null && !known.IsStale(now, MaxAge))
            return known.Exists ? new JobCategory(known.CategoryId!, known.Label) : null;

        // Outage throws here → nothing is stored (a wrong "doesn't exist" would stick for 90 days)
        var category = await source.ResolveCategoryAsync(label, ct);

        if (known is null)
            db.SourceCategories.Add(new SourceCategory(source.Name, label, category?.Id, now));
        else
            known.Recheck(category?.Id, now);

        await db.SaveChangesAsync(ct);
        return category;
    }

    /// <summary>Verified categories of a source (learned + built-in) – the AI must prefer these for a stable mapping.</summary>
    public async Task<IReadOnlyList<JobCategory>> KnownAsync(IJobSource source, CancellationToken ct = default)
    {
        var learned = await db.SourceCategories
            .Where(c => c.Source == source.Name && c.CategoryId != null)
            .ToListAsync(ct);

        return learned
            .Select(c => new JobCategory(c.CategoryId!, c.Label))
            .Concat(source.Categories)
            .DistinctBy(c => c.Id)
            .OrderBy(c => c.Label, StringComparer.Create(new System.Globalization.CultureInfo("cs-CZ"), ignoreCase: true))
            .ToList();
    }
}