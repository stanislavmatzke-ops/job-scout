using JobScout.Application.Abstractions;
using JobScout.Application.Llm;
using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Profiles;

public sealed record ProfileView(
    int? Version, string? CvFileName, string? Notes, DateTimeOffset? AnalyzedAt,
    CandidateProfile? Profile, GrammaticalGender Gender, bool HasCv);

/// <summary>"Můj profil": the newest analysed version + settings. A new user simply has nothing yet.</summary>
public sealed class GetProfile(IAppDbContext db)
{
    public async Task<ProfileView> ExecuteAsync(Guid userId, CancellationToken ct = default)
    {
        var gender = await db.Users.Where(u => u.Id == userId).Select(u => u.Gender).SingleAsync(ct);
        var hasCv = await db.StoredCvs.AnyAsync(c => c.UserId == userId, ct);
        var latest = await db.UserProfiles.AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.Version)
            .FirstOrDefaultAsync(ct);

        return latest is null
            ? new ProfileView(null, null, null, null, null, gender, hasCv)
            : new ProfileView(latest.Version, latest.CvFileName, latest.Notes, latest.CreatedAt,
                LlmJson.Deserialize<CandidateProfile>(latest.ProfileJson), gender, hasCv);
    }
}
