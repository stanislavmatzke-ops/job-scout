using JobScout.Application.Abstractions;
using JobScout.Application.Llm;
using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Profiles;

/// <summary>Who is asking (identity from SSO, or "dev" persona) and what they uploaded.</summary>
public sealed record ProfileInput(
    string Provider, string Subject, string DisplayName,
    string CvFileName, LlmDocument Cv, string? Notes);

/// <summary>Result: the user, the active profile version and the parsed profile. Response = null → AI was not called.</summary>
public sealed record AnalyzedProfile(User User, UserProfile Version, CandidateProfile Profile, LlmResponse? Response)
{
    public bool IsNew => Response is not null;
}

/// <summary>
/// Use case "analyse once": finds or creates the user, and calls the AI only when CV or notes changed
/// (input hash differs from the latest version). Otherwise returns the stored profile – zero tokens.
/// </summary>
public sealed class AnalyzeProfile(IAppDbContext db, ProfileExtractor extractor, TimeProvider time)
{
    public async Task<AnalyzedProfile> ExecuteAsync(ProfileInput input, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();

        var user = await db.Users
            .SingleOrDefaultAsync(u => u.Provider == input.Provider && u.Subject == input.Subject, ct);
        if (user is null)
        {
            user = new User(input.Provider, input.Subject, email: "", input.DisplayName, now);
            db.Users.Add(user);
        }

        var hash = UserProfile.ComputeInputHash(input.Cv.Content, input.Notes);
        var latest = await db.UserProfiles
            .Where(p => p.UserId == user.Id)
            .OrderByDescending(p => p.Version)
            .FirstOrDefaultAsync(ct);

        if (latest is not null && latest.InputHash == hash)
            return new AnalyzedProfile(user, latest, LlmJson.Deserialize<CandidateProfile>(latest.ProfileJson), Response: null);

        var extraction = await extractor.ExtractAsync(input.Cv, input.Notes, ct);
        var version = new UserProfile(
            user.Id, (latest?.Version ?? 0) + 1, input.CvFileName, input.Notes, hash,
            LlmJson.Serialize(extraction.Profile), extraction.Response.Model, now);

        db.UserProfiles.Add(version);
        await db.SaveChangesAsync(ct);

        return new AnalyzedProfile(user, version, extraction.Profile, extraction.Response);
    }
}