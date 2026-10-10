using JobScout.Application.Jobs;

namespace JobScout.Infrastructure.JobsCz;

/// <summary>jobs.cz professions/fields usable in the URL. Verified 6 Oct 2026 – an unknown slug returns 404.</summary>
internal static class JobsCzCatalog
{
    public static readonly IReadOnlyList<JobCategory> Categories =
    [
        new("is-it-vyvoj-aplikaci-a-systemu", "Obor IS/IT: Vývoj aplikací a systémů"),
        new("is-it-konzultace-analyzy-a-projektove-rizeni", "Obor IS/IT: Konzultace, analýzy a projektové řízení"),
        new("sw-architekt", "SW architekt"),
        new("vedouci-vyvoje-team-leader", "Vedoucí vývoje / Team Leader"),
        new("net-vyvojar", ".NET vývojář"),
        new("java-vyvojar", "Java vývojář"),
        new("react-vyvojar", "React vývojář"),
        new("programator", "Programátor"),
        new("it-konzultant", "IT konzultant"),
        new("it-analytik", "IT analytik"),
        new("databazovy-specialista", "Databázový specialista"),
    ];
}