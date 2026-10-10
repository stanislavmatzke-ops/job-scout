using System.Collections.Concurrent;
using System.Threading.Channels;
using JobScout.Application.Abstractions;
using JobScout.Application.Scouting;

namespace JobScout.Api.Scouting;

/// <summary>
/// In-memory queue of searches (6h). Good enough for one API instance: a restart loses what waits,
/// and the morning run catches it up. A shared queue (Azure Storage Queue / Service Bus) only with more instances.
/// </summary>
public sealed class ScoutQueue : IScoutQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<Guid, byte> _busy = new();

    public bool TryEnqueue(Guid userId)
    {
        if (!_busy.TryAdd(userId, 0))
            return false;                         // already waiting or running
        _channel.Writer.TryWrite(userId);         // unbounded – always succeeds
        return true;
    }

    public bool IsBusy(Guid userId) => _busy.ContainsKey(userId);

    internal ChannelReader<Guid> Reader => _channel.Reader;

    internal void Done(Guid userId) => _busy.TryRemove(userId, out _);
}

/// <summary>Scout:FirstSearch:Enabled=false (the API tests) – nothing is ever queued.</summary>
public sealed class DisabledScoutQueue : IScoutQueue
{
    public bool TryEnqueue(Guid userId) => false;
    public bool IsBusy(Guid userId) => false;
}

/// <summary>
/// Takes the queued users one by one – the same pipeline as the morning run (PrepareScoutProfile → RunScout),
/// a fresh scope (DbContext) per user. One at a time: Playwright and the AI rate limits don't like a crowd.
/// </summary>
public sealed class ScoutQueueWorker(ScoutQueue queue, IServiceScopeFactory scopeFactory, ILogger<ScoutQueueWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var userId in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var prepared = await scope.ServiceProvider.GetRequiredService<PrepareScoutProfile>().ExecuteAsync(userId, stoppingToken);
                if (prepared is null)
                    continue;

                logger.LogInformation("První hledání pro uživatele {UserId} – profil v{Version}", userId, prepared.Version.Version);
                var result = await scope.ServiceProvider.GetRequiredService<RunScout>().ExecuteAsync(prepared, stoppingToken);
                logger.LogInformation("První hledání hotovo: nalezeno {Found}, ohodnoceno {Evaluated}, cena ${Cost:0.000}",
                    result.Recorded.Found, result.Evaluation.Evaluated.Count, result.Cost.TotalUsd);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // RunScout already recorded the failure in the journal; the next CV upload (or the morning) tries again
                logger.LogError(ex, "První hledání pro uživatele {UserId} selhalo", userId);
            }
            finally
            {
                queue.Done(userId);
            }
        }
    }
}

public static class ScoutQueueSetup
{
    public static IServiceCollection AddFirstSearch(this IServiceCollection services, IConfiguration config)
    {
        services.AddScoped<StartFirstScout>();
        services.AddScoped<GetScoutStatus>();

        if (!config.GetValue("Scout:FirstSearch:Enabled", defaultValue: true))
        {
            services.AddSingleton<IScoutQueue, DisabledScoutQueue>();
            return services;
        }

        services.AddSingleton<ScoutQueue>();
        services.AddSingleton<IScoutQueue>(sp => sp.GetRequiredService<ScoutQueue>());
        services.AddHostedService<ScoutQueueWorker>();
        return services;
    }
}
