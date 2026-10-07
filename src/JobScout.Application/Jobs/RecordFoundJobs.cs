using JobScout.Application.Abstractions;
using JobScout.Core.Domain;
using JobScout.Core.Jobs;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Jobs;

/// <summary>A job new for this user: the shared ad and the user's fresh match.</summary>
public sealed record FoundJob(JobMatch Match, JobOffer Offer);

public sealed record RecordFoundJobsResult(int Found, int NewOffers, int AlreadySeen, IReadOnlyList<FoundJob> NewForUser);

/// <summary>
/// Search results → shared JobOffers (created or refreshed) + a JobMatch for every ad the user hasn't seen yet.
/// "Seen" = any existing match, whatever its stage or status – so Hidden / refreshed ads never come back.
/// </summary>
public sealed class RecordFoundJobs(IAppDbContext db, TimeProvider time)
{
    public async Task<RecordFoundJobsResult> ExecuteAsync(
        Guid userId, int profileVersion, IReadOnlyList<JobListing> listings, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var unique = listings.DistinctBy(l => (l.Source, l.ExternalId)).ToList();
        if (unique.Count == 0)
            return new RecordFoundJobsResult(0, 0, 0, []);

        // 1) Shared offers – one query per source for all IDs (not one query per ad = no N+1)
        var offers = new List<JobOffer>();
        var newOffers = 0;
        foreach (var group in unique.GroupBy(l => l.Source))
        {
            var ids = group.Select(l => l.ExternalId).ToList();
            var existing = await db.JobOffers
                .Where(o => o.Source == group.Key && ids.Contains(o.ExternalId))
                .ToDictionaryAsync(o => o.ExternalId, ct);

            foreach (var listing in group)
            {
                if (existing.TryGetValue(listing.ExternalId, out var offer))
                    offer.Refresh(listing, now);
                else
                {
                    offer = JobOffer.FromListing(listing, now);
                    db.JobOffers.Add(offer);
                    newOffers++;
                }
                offers.Add(offer);
            }
        }

        // 2) Which of them has this user already seen – in ANY stage/status
        var offerIds = offers.Select(o => o.Id).ToList();
        var seen = (await db.JobMatches
                .Where(m => m.UserId == userId && offerIds.Contains(m.JobOfferId))
                .Select(m => m.JobOfferId)
                .ToListAsync(ct))
            .ToHashSet();

        var fresh = offers
            .Where(o => !seen.Contains(o.Id))
            .Select(o => new FoundJob(new JobMatch(userId, o.Id, profileVersion, now), o))
            .ToList();

        db.JobMatches.AddRange(fresh.Select(f => f.Match));
        await db.SaveChangesAsync(ct);   // offers + matches in one transaction

        return new RecordFoundJobsResult(unique.Count, newOffers, unique.Count - fresh.Count, fresh);
    }

    /// <summary>First run for a user: last 3 days; afterwards only the last day (the morning run).</summary>
    public async Task<JobSearch.JobAge> SearchWindowAsync(Guid userId, CancellationToken ct = default) =>
        await db.JobMatches.AnyAsync(m => m.UserId == userId, ct)
            ? JobSearch.JobAge.Day
            : JobSearch.JobAge.ThreeDays;
}