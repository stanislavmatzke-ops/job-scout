using JobScout.Application.Abstractions;
using JobScout.Application.Llm;
using JobScout.Application.Profiles;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Scouting;

/// <summary>
/// This morning's profile, from the DB (not from files): stored CV + latest notes go through AnalyzeProfile –
/// zero tokens when nothing changed, a fresh analysis after a prompt version bump.
/// Entities stay tracked: RunScout saves the search plan on the profile version.
/// </summary>
public sealed class PrepareScoutProfile(IAppDbContext db, AnalyzeProfile analyze)
{
    /// <returns>null = the user has no profile yet</returns>
    public async Task<AnalyzedProfile?> ExecuteAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);
        var latest = await db.UserProfiles
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.Version)
            .FirstOrDefaultAsync(ct);
        if (latest is null)
            return null;

        var cv = await db.StoredCvs.AsNoTracking().SingleOrDefaultAsync(c => c.UserId == userId, ct);
        if (cv is null)          // profiles from before CVs were stored (5d) – use them as they are
            return new AnalyzedProfile(user, latest, LlmJson.Deserialize<CandidateProfile>(latest.ProfileJson), Response: null);

        return await analyze.ExecuteAsync(new ProfileInput(
            user.Provider, user.Subject, user.DisplayName, cv.FileName,
            new LlmDocument("CV", cv.MediaType, cv.Content), latest.Notes), ct);
    }
}
