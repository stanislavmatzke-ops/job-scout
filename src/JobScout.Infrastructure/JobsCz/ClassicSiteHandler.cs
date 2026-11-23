using Microsoft.Extensions.Logging;

namespace JobScout.Infrastructure.JobsCz;

/// <summary>
/// jobs.cz tests a new site: a random part of the visitors (≈ 1 in 5 in Nov 2026, decided by the cookie beta_bucket)
/// is redirected to beta.jobs.cz – a different application with other ad ids (/nabidka/&lt;uuid&gt; instead of /rpd/&lt;id&gt;),
/// most results rendered by JavaScript and without our date filter. Our parser reads the classic site only.
/// Two parts: the HttpClient keeps no cookies (registered in DependencyInjection), so one unlucky first request no
/// longer sends a whole morning run to the beta (13 and 23 Nov 2026: every search failed); and a request that still
/// ends up there is asked again – after a growing pause, because jobs.cz keeps sending the same address to the beta for
/// a while (23 Nov: four tries two seconds apart all landed there). Every retry also waits in the PoliteDelayHandler.
/// A search that stays on the beta is skipped by the run, not fatal (RunScout).
/// </summary>
internal sealed class ClassicSiteHandler(ILogger<ClassicSiteHandler> logger, IReadOnlyList<TimeSpan>? pauses = null)
    : DelegatingHandler
{
    public const string BetaHost = "beta.jobs.cz";

    /// <summary>Before the 2nd, 3rd and 4th attempt – under a minute in all (the HttpClient's timeout covers it).</summary>
    public static readonly IReadOnlyList<TimeSpan> DefaultPauses =
        [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30)];

    private readonly IReadOnlyList<TimeSpan> _pauses = pauses ?? DefaultPauses;

    public int MaxAttempts => _pauses.Count + 1;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var response = await base.SendAsync(attempt == 1 ? request : Copy(request), ct);
            if (response.RequestMessage?.RequestUri?.Host != BetaHost || attempt == MaxAttempts)
                return response;     // the classic site – or give up: the parser then reports the page it doesn't know

            logger.LogInformation("jobs.cz: {Url} přesměroval na betu – zkusím znovu za {Pause} s ({Attempt}/{Max})",
                request.RequestUri, _pauses[attempt - 1].TotalSeconds, attempt + 1, MaxAttempts);
            response.Dispose();
            await Task.Delay(_pauses[attempt - 1], ct);
        }
    }

    /// <summary>A request can be sent only once; ours are plain GETs without a body.</summary>
    private static HttpRequestMessage Copy(HttpRequestMessage original)
    {
        var copy = new HttpRequestMessage(original.Method, original.RequestUri) { Version = original.Version };
        foreach (var header in original.Headers)
            copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return copy;
    }
}
