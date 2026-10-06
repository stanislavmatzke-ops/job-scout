using JobScout.Core.Jobs;
using JobScout.Infrastructure.Browser;
using Microsoft.Extensions.Logging;

namespace JobScout.Infrastructure.JobsCz;

internal sealed class JobsCzSource(HttpClient http, IPageRenderer renderer, ILogger<JobsCzSource> logger) : IJobSource
{
    public string Name => JobsCzParser.SourceName;
    // Career pages render their content with JS – wait until this element exists
    private const string CareerPageReadySelector = ".cp-detail__content";

    public async Task<IReadOnlyList<JobListing>> SearchAsync(JobSearchQuery query, CancellationToken ct = default)
    {
        var url = BuildSearchUrl(query);
        logger.LogInformation("jobs.cz: hledám {Url}", url);

        var html = await http.GetStringAsync(url, ct);
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
    
    // /prace/praha/?q%5B%5D=.NET%20architekt&page=2   (q%5B%5D = "q[]")
    internal static string BuildSearchUrl(JobSearchQuery q)
    {
        var path = string.IsNullOrWhiteSpace(q.Location) ? "/prace/" : $"/prace/{q.Location.Trim('/')}/";
        var url = $"{path}?q%5B%5D={Uri.EscapeDataString(q.Keywords)}";
        return q.Page > 1 ? $"{url}&page={q.Page}" : url;
    }
}