using JobScout.Core.Jobs;
using Microsoft.Extensions.Logging;

namespace JobScout.Infrastructure.JobsCz;

internal sealed class JobsCzSource(HttpClient http, ILogger<JobsCzSource> logger) : IJobSource
{
    public string Name => JobsCzParser.SourceName;

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

        // HttpClient follows redirects automatically – RequestUri is the final address
        var finalUrl = response.RequestMessage?.RequestUri ?? new Uri(http.BaseAddress!, $"/rpd/{externalId}/");
        var html = await response.Content.ReadAsStringAsync(ct);

        var detail = JobsCzParser.ParseDetail(html, externalId, finalUrl);
        logger.LogInformation("jobs.cz: detail {Id} – {Status}, {Length} znaků{Redirect}",
            externalId, detail.IsComplete ? "úplný" : "NEÚPLNÝ", detail.Text.Length,
            finalUrl.Host == "www.jobs.cz" ? "" : $", přesměrováno na {finalUrl.Host}");
        return detail;
    }
    
    // /prace/praha/?q%5B%5D=.NET%20architekt&page=2   (q%5B%5D = "q[]")
    internal static string BuildSearchUrl(JobSearchQuery q)
    {
        var path = string.IsNullOrWhiteSpace(q.Location) ? "/prace/" : $"/prace/{q.Location.Trim('/')}/";
        var url = $"{path}?q%5B%5D={Uri.EscapeDataString(q.Keywords)}";
        return q.Page > 1 ? $"{url}&page={q.Page}" : url;
    }
}