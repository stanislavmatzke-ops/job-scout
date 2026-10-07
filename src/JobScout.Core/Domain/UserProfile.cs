using System.Security.Cryptography;
using System.Text;

namespace JobScout.Core.Domain;

/// <summary>
/// One analysed version of a user's CV + notes. A new version is created only when the input changes
/// (InputHash) – that's the "analyse once" rule. JobMatch remembers which version evaluated it.
/// </summary>
public sealed class UserProfile
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public int Version { get; private set; }
    public string CvFileName { get; private set; } = "";
    public string? Notes { get; private set; }
    public string InputHash { get; private set; } = "";
    public string ProfileJson { get; private set; } = "";      // CandidateProfile from AI
    public string? SearchPlanJson { get; private set; }        // added in phase 3c
    public string Model { get; private set; } = "";            // which LLM produced it
    public DateTimeOffset CreatedAt { get; private set; }

    private UserProfile() { }

    public UserProfile(Guid userId, int version, string cvFileName, string? notes, string inputHash,
        string profileJson, string model, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        UserId = userId;
        Version = version;
        CvFileName = cvFileName;
        Notes = notes;
        InputHash = inputHash;
        ProfileJson = profileJson;
        Model = model;
        CreatedAt = now;
    }

    public void SetSearchPlan(string searchPlanJson) => SearchPlanJson = searchPlanJson;

    /// <summary>Fingerprint of the input: same CV bytes + same notes = same hash = no new AI analysis.</summary>
    public static string ComputeInputHash(byte[] cv, string? notes)
    {
        var notesBytes = Encoding.UTF8.GetBytes(notes?.Trim() ?? "");
        return Convert.ToHexString(SHA256.HashData([.. cv, .. notesBytes]));
    }
    
    /// <summary>
    /// Fingerprint of the input: same CV bytes + same notes + same analysis version = no new AI analysis.
    /// The version (prompt/schema) makes stored profiles re-analyse automatically after a prompt change.
    /// </summary>
    public static string ComputeInputHash(byte[] cv, string? notes, int analysisVersion)
    {
        var tail = Encoding.UTF8.GetBytes($"{notes?.Trim()}\n#analysis-v{analysisVersion}");
        return Convert.ToHexString(SHA256.HashData([.. cv, .. tail]));
    }
}