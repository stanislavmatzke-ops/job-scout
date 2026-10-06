using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using JobScout.Core.Jobs;
using JobScout.Infrastructure.Browser;
using Microsoft.Extensions.Logging;

namespace JobScout.Infrastructure.JobsCz;

internal sealed partial class JobsCzSource(HttpClient http, IPageRenderer renderer, ILogger<JobsCzSource> logger) : IJobSource
{
    public IReadOnlyList<JobCategory> Categories => JobsCzCatalog.Categories;
    private const int PageSize = 30;
    public string Name => JobsCzParser.SourceName;
    // Career pages render their content with JS – wait until this element exists
    private const string CareerPageReadySelector = ".cp-detail__content";

    public async Task<IReadOnlyList<JobListing>> SearchAsync(JobSearchQuery query, int maxPages = 1, CancellationToken ct = default)
    {
        var all = new Dictionary<string, JobListing>();   // keeps order of first appearance

        for (var page = 1; page <= maxPages; page++)
        {
            var listings = await SearchPageAsync(query with { Page = page }, ct);
            var added = listings.Count(l => all.TryAdd(l.ExternalId, l));

            // Last page: fewer than a full page, or jobs.cz repeating the last page (no new IDs)
            if (listings.Count < PageSize || added == 0)
                break;
        }

        return all.Values.ToList();
    }
    
    private async Task<IReadOnlyList<JobListing>> SearchPageAsync(JobSearchQuery query, CancellationToken ct = default)
    {
        var url = BuildSearchUrl(query);
        logger.LogInformation("jobs.cz: hledám {Url}", url);

        using var response = await http.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // e.g. an unknown location slug – skip this search, don't fail the whole morning run
            logger.LogWarning("jobs.cz: {Url} neexistuje (404) – hledání přeskakuji", url);
            return [];
        }
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(ct);
        var listings = JobsCzParser.ParseSearchResults(html);

        logger.LogInformation("jobs.cz: nalezeno {Count} nabídek", listings.Count);
        return listings;
    }

    public async Task<JobDetail> GetDetailAsync(string externalId, CancellationToken ct = default)
    {
        using var response = await http.GetAsync($"/rpd/{externalId}/", ct);
        response.EnsureSuccessStatusCode();

        var finalUrl = response.RequestMessage?.RequestUri ?? new Uri(http.BaseAddress!, $"/rpd/{externalId}/");
        var html = await response.Content.ReadAsStringAsync(ct);
        var redirected = finalUrl.Host != "www.jobs.cz";

        var detail = redirected
            ? await RenderCareerPageAsync(externalId, finalUrl, ct) ?? JobsCzParser.ParseDetail(html, externalId, finalUrl)
            : JobsCzParser.ParseDetail(html, externalId, finalUrl);

        logger.LogInformation("jobs.cz: detail {Id} – {Status}, {Length} znaků{Redirect}",
            externalId, detail.IsComplete ? "úplný" : "NEÚPLNÝ", detail.Text.Length,
            redirected ? $", kariérní stránka {finalUrl.Host}" : "");
        return detail;
    }

    private async Task<JobDetail?> RenderCareerPageAsync(string externalId, Uri url, CancellationToken ct)
    {
        try
        {
            var html = await renderer.RenderAsync(url, CareerPageReadySelector, ct);
            var detail = JobsCzParser.ParseCareerPage(html, externalId, url);
            if (detail is null)
                logger.LogWarning("jobs.cz: kariérní stránka {Host} má neznámou šablonu – použiju útržek", url.Host);
            return detail;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Timeout or browser error must not drop the job – fall back to the snippet
            logger.LogWarning(ex, "jobs.cz: vykreslení {Url} selhalo – použiju útržek", url);
            return null;
        }
    }
    
    internal static string BuildSearchUrl(JobSearchQuery q)
    {
        var path = "/prace/";
        if (!string.IsNullOrWhiteSpace(q.Location)) path += ToSlug(q.Location) + "/";
        if (!string.IsNullOrWhiteSpace(q.Category)) path += q.Category.Trim('/') + "/";

        var parameters = new List<string>();
        if (!string.IsNullOrWhiteSpace(q.Keywords)) parameters.Add($"q%5B%5D={Uri.EscapeDataString(q.Keywords)}");
        if (q.MaxAge != JobAge.Any) parameters.Add($"date={(q.MaxAge == JobAge.Day ? "24h" : "3d")}");
        if (q.Page > 1) parameters.Add($"page={q.Page}");

        return parameters.Count == 0 ? path : $"{path}?{string.Join('&', parameters)}";
    }
    
    // "Hradec Králové" → "hradec-kralove" (jobs.cz location URL format)
    internal static string ToSlug(string location)
    {
        var decomposed = location.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;                                   // drop diacritics: "á" = "a" + "´"
            sb.Append(char.IsLetterOrDigit(c) ? c : '-');
        }
        return DashRuns().Replace(sb.ToString(), "-").Trim('-');
    }

    [GeneratedRegex("-+")]
    private static partial Regex DashRuns();
}