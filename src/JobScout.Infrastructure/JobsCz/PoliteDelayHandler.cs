namespace JobScout.Infrastructure.JobsCz;

/// <summary>At most one request at a time, with a minimum gap between them – be gentle to the site.</summary>
internal sealed class PoliteDelayHandler(TimeSpan minGap) : DelegatingHandler
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var wait = _lastRequest + minGap - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, ct);

            return await base.SendAsync(request, ct);
        }
        finally
        {
            _lastRequest = DateTimeOffset.UtcNow;
            _gate.Release();
        }
    }
}