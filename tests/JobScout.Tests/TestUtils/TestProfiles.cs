using JobScout.Application.Profiles;
using JobScout.Core;

namespace JobScout.Tests.TestUtils;

/// <summary>Small candidate profiles for planner tests – only the parts the planner reads matter.</summary>
internal static class TestProfiles
{
    public static CandidateProfile With(
        IReadOnlyList<string> professions,
        string homeLocation = "Kolín",
        int radiusKm = 30,
        IReadOnlyList<string>? extraLocations = null,
        IReadOnlyList<RemoteWork>? acceptableRemote = null) =>
        new(
            Headline: "Testovací profil",
            YearsOfExperience: 10,
            Seniority: Seniority.Senior,
            TargetRoles: professions,
            CoreSkills: [],
            OtherSkills: [],
            Domains: [],
            Languages: [],
            Preferences: new WorkPreferences(
                RemoteWork: RemoteWork.Onsite,
                AcceptableRemoteWork: acceptableRemote ?? [RemoteWork.Onsite],
                EmploymentTypes: [EmploymentType.Employee],
                MinMonthlySalaryCzk: 40000,
                Priorities: [],
                Avoid: []),
            Search: new SearchIntent(
                Professions: professions,
                Keywords: [],
                HomeLocation: homeLocation,
                CommuteRadiusKm: radiusKm,
                ExtraLocations: extraLocations ?? []));
}
