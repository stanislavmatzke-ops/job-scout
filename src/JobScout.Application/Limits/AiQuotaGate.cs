using System.Collections.Concurrent;

namespace JobScout.Application.Limits;

/// <summary>
/// Singleton (ADR-0021). One AI click per user at a time: without it two fast requests would both see
/// "2 of 3 used" and both go through. In memory is enough – the API runs as a single replica (ADR-0019).
/// Also remembers when the admins were last told that the app-wide brake engaged, so they get one e-mail, not one per click.
/// </summary>
public sealed class AiQuotaGate
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _users = new();
    private readonly Lock _alertLock = new();
    private DateTimeOffset? _lastPausedAlert;

    public async Task<IDisposable> EnterAsync(Guid userId, CancellationToken ct = default)
    {
        var semaphore = _users.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct);
        return new Release(semaphore);
    }

    /// <returns>true = tell the admins now (and not again within the window)</returns>
    public bool ShouldAlertPaused(DateTimeOffset now)
    {
        lock (_alertLock)
        {
            if (_lastPausedAlert is { } last && now - last < AiLimitOptions.Window)
                return false;
            _lastPausedAlert = now;
            return true;
        }
    }

    private sealed class Release(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                semaphore.Release();
        }
    }
}
