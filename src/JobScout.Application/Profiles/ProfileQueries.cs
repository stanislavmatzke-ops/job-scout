using JobScout.Application.Abstractions;
using JobScout.Application.Llm;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Profiles;

public static class ProfileQueries
{
    /// <summary>The user's newest analysed profile, or null when they haven't uploaded a CV yet.</summary>
    public static async Task<CandidateProfile?> CurrentProfileAsync(this IAppDbContext db, Guid userId, CancellationToken ct = default)
    {
        var json = await db.UserProfiles
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.Version)
            .Select(p => p.ProfileJson)
            .FirstOrDefaultAsync(ct);

        return json is null ? null : LlmJson.Deserialize<CandidateProfile>(json);
    }
}
