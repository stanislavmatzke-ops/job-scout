using JobScout.Application.Abstractions;
using JobScout.Application.Llm;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Profiles;

public sealed record CurrentProfile(int Version, CandidateProfile Profile);

public static class ProfileQueries
{
    /// <summary>The user's newest analysed profile and its version, or null when they haven't uploaded a CV yet.</summary>
    public static async Task<CurrentProfile?> CurrentProfileAsync(this IAppDbContext db, Guid userId, CancellationToken ct = default)
    {
        var latest = await db.UserProfiles
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.Version)
            .Select(p => new { p.Version, p.ProfileJson })
            .FirstOrDefaultAsync(ct);

        return latest is null ? null : new CurrentProfile(latest.Version, LlmJson.Deserialize<CandidateProfile>(latest.ProfileJson));
    }

    /// <summary>Just the number – for marking outdated evaluations without loading the profile.</summary>
    public static Task<int?> CurrentProfileVersionAsync(this IAppDbContext db, Guid userId, CancellationToken ct = default) =>
        db.UserProfiles.Where(p => p.UserId == userId).MaxAsync(p => (int?)p.Version, ct);
}
