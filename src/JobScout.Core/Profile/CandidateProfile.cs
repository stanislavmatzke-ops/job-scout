namespace JobScout.Core.Profile;

public enum Seniority { Junior, Medior, Senior, Lead, Architect }

/// <summary>
/// Structured candidate profile extracted from the CV and the candidate's notes.
/// Intentionally contains no name or contact details – only what is needed for matching (GDPR: data minimization).
/// </summary>
public sealed record CandidateProfile(
    string Headline,
    int YearsOfExperience,
    Seniority Seniority,
    IReadOnlyList<string> TargetRoles,
    IReadOnlyList<string> SearchKeywords,   // phrases for job portal search, most important first
    IReadOnlyList<string> CoreSkills,
    IReadOnlyList<string> OtherSkills,
    IReadOnlyList<string> Domains,
    IReadOnlyList<string> Languages,
    WorkPreferences Preferences);

public sealed record WorkPreferences(
    RemoteWork RemoteWork,                          // ideal
    IReadOnlyList<RemoteWork> AcceptableRemoteWork, // still acceptable (e.g. ideal full remote, hybrid OK)
    IReadOnlyList<EmploymentType> EmploymentTypes,
    int MinMonthlySalaryCzk,
    IReadOnlyList<string> Locations,
    IReadOnlyList<string> Priorities,
    IReadOnlyList<string> Avoid);
