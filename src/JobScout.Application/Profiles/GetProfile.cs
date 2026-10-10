using JobScout.Application.Abstractions;
using JobScout.Application.Llm;
using JobScout.Application.Matches;
using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Profiles;

public sealed record ProfileView(
    int? Version, string? CvFileName, string? Notes, DateTimeOffset? AnalyzedAt,
    CandidateProfile? Profile, GrammaticalGender Gender, bool HasCv,
    int OutdatedEvaluations);                       // open jobs evaluated with an older profile version (6d)

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

        if (latest is null)
            return new ProfileView(null, null, null, null, null, gender, hasCv, OutdatedEvaluations: 0);

        var outdated = await db.JobMatches
            .Where(m => m.UserId == userId && m.Stage == MatchStage.Evaluated && m.ProfileVersion < latest.Version)
            .Where(MatchViews.Filter(MatchView.Open))          // only what is still waiting for a decision
            .CountAsync(ct);

        return new ProfileView(latest.Version, latest.CvFileName, latest.Notes, latest.CreatedAt,
            LlmJson.Deserialize<CandidateProfile>(latest.ProfileJson), gender, hasCv, outdated);
    }
}
