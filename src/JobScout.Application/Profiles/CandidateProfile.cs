using JobScout.Core;

namespace JobScout.Application.Profiles;

/// <summary>Profession-neutral scale (ADR-0014): works for a car painter as well as for an architect.</summary>
public enum Seniority { Junior, Medior, Senior, Lead, Expert }

/// <summary>
/// Structured candidate profile extracted from the CV and the candidate's notes.
/// Intentionally contains no name or contact details – only what is needed for matching (GDPR: data minimization).
/// </summary>
public sealed record CandidateProfile(
    string Headline,
    int YearsOfExperience,
    Seniority Seniority,
    IReadOnlyList<string> TargetRoles,
    IReadOnlyList<string> CoreSkills,
    IReadOnlyList<string> OtherSkills,
    IReadOnlyList<string> Domains,
    IReadOnlyList<string> Languages,
    WorkPreferences Preferences,
    SearchIntent Search);

public sealed record WorkPreferences(
    RemoteWork RemoteWork,                          // ideal
    IReadOnlyList<RemoteWork> AcceptableRemoteWork, // still acceptable (e.g. ideal full remote, hybrid OK)
    IReadOnlyList<EmploymentType> EmploymentTypes,
    int MinMonthlySalaryCzk,
    IReadOnlyList<string> Priorities,
    IReadOnlyList<string> Avoid);

/// <summary>
/// What and where to search – independent of any job portal (ADR-0012).
/// Each source translates it into its own search plan (categories, location slugs, radius).
/// </summary>
public sealed record SearchIntent(
    IReadOnlyList<string> Professions,    // job titles as advertised, most important first
    IReadOnlyList<string> Keywords,       // optional narrowing phrases (skill, technology, material) – not job titles
    string HomeLocation,                  // town the candidate commutes from; "" = unknown
    int CommuteRadiusKm,                  // 0 = not stated
    IReadOnlyList<string> ExtraLocations); // explicitly accepted places outside the radius