using JobScout.Application.Llm;
using JobScout.Core.Domain;
using JobScout.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobScout.Infrastructure.Claude;

/// <summary>
/// Decorator (ADR-0016): records every call as an LlmCall in its own DbContext – survives a failing use case.
/// Recording never breaks the actual AI call.
/// </summary>
internal sealed class UsageRecordingLlmClient(
    ILlmClient inner,
    IDbContextFactory<JobScoutDbContext> dbFactory,
    IOptions<ClaudeOptions> options,
    TimeProvider time,
    ILogger<UsageRecordingLlmClient> logger) : ILlmClient
{
    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        var context = LlmCallContext.Current;
        var started = time.GetTimestamp();

        try
        {
            var response = await inner.CompleteAsync(request, ct);

            var cost = LlmPricing.CostMicroUsd(response.Model, response.InputTokens, response.OutputTokens, options.Value.Pricing);
            if (cost is null)
                logger.LogWarning("Neznámá cena modelu {Model} – náklady se započítají jako 0", response.Model);

            await RecordAsync(LlmCall.Success(context?.UserId, context?.ScoutRunId, request.Purpose.ToString(),
                response.Model, response.InputTokens, response.OutputTokens, cost ?? 0, ElapsedMs(started), time.GetUtcNow()));
            return response;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await RecordAsync(LlmCall.Failure(context?.UserId, context?.ScoutRunId, request.Purpose.ToString(),
                request.Tier.ToString(), $"{ex.GetType().Name}: {ex.Message}", ElapsedMs(started), time.GetUtcNow()));
            throw;
        }
    }

    private int ElapsedMs(long started) => (int)time.GetElapsedTime(started).TotalMilliseconds;

    private async Task RecordAsync(LlmCall call)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            db.LlmCalls.Add(call);
            await db.SaveChangesAsync();          // no CancellationToken: a paid call is recorded even while shutting down
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Záznam volání AI ({Purpose}) se nepodařilo uložit", call.Purpose);
        }
    }
}