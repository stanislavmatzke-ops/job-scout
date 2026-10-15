using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using JobScout.Application.Jobs;
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

    /// <summary>https://www.jobs.cz/rpd/2001234567/?searchId=… → "2001234567" – the same id the search results carry.</summary>
    public string? ExternalIdFromUrl(Uri url)
    {
        if (url.Scheme is not ("http" or "https") || url.Host is not ("www.jobs.cz" or "jobs.cz"))
            return null;
        var match = AdPath().Match(url.AbsolutePath);
        return match.Success ? match.Groups[1].Value : null;
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
        if (q.RadiusKm > 0 && !string.IsNullOrWhiteSpace(q.Location))
            parameters.Add($"locality%5Bradius%5D={q.RadiusKm}");
        if (q.MaxAge != JobAge.Any) parameters.Add($"date={(q.MaxAge == JobAge.Day ? "24h" : "3d")}");
        if (q.Page > 1) parameters.Add($"page={q.Page}");

        return parameters.Count == 0 ? path : $"{path}?{string.Join('&', parameters)}";
    }
    
    // "Hradec Králové" → "hradec-kralove" (jobs.cz location URL format)
    internal static string ToSlug(string text)
    {
        var decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;                                   // drop diacritics: "á" = "a" + "´"
            sb.Append(char.IsLetterOrDigit(c) ? c : '-');
        }
        return DashRuns().Replace(sb.ToString(), "-").Trim('-');
    }
    
    public async Task<JobCategory?> ResolveCategoryAsync(string label, CancellationToken ct = default)
    {
        var slug = ToSlug(label);
        if (slug.Length == 0)
            return null;

        // Already verified IT categories – no request needed
        var known = JobsCzCatalog.Categories.FirstOrDefault(c => c.Id == slug);
        if (known is not null)
            return known;

        var path = $"/prace/{slug}/";
        using var response = await http.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogInformation("jobs.cz: profese '{Label}' ({Slug}) neexistuje", label.Trim(), slug);
            return null;
        }
        response.EnsureSuccessStatusCode();   // 5xx = outage, not "doesn't exist" – don't store a wrong plan

        // 200 after a redirect elsewhere (e.g. to /prace/) is not this profession
        if (response.RequestMessage?.RequestUri?.AbsolutePath != path)
            return null;

        logger.LogInformation("jobs.cz: profese '{Label}' → {Slug}", label.Trim(), slug);
        return new JobCategory(slug, label.Trim());
    }

    [GeneratedRegex(@"^/rpd/(\d{5,20})/?$")]
    private static partial Regex AdPath();

    [GeneratedRegex("-+")]
    private static partial Regex DashRuns();
}