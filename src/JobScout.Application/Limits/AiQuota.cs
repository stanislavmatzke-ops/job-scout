using JobScout.Application.Abstractions;
using JobScout.Application.Llm;
using JobScout.Application.Notifications;
using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace JobScout.Application.Limits;

/// <summary>
/// AI limits for clicks (ADR-0021). Counts from the cost records we keep anyway (LlmCall, ADR-0016): a click is a call
/// for a user that belongs to no morning run. No counters to keep in sync, nothing to reset at midnight –
/// a sliding 24-hour window. Admins have no limits.
/// </summary>
public sealed class AiQuota(
    IAppDbContext db,
    AiLimitOptions options,
    AiQuotaGate gate,
    TimeProvider time,
    ILogger<AiQuota> logger,
    IAdminDirectory? admins = null,
    Notifier? notifier = null)
{
    private readonly Dictionary<Guid, bool> _exempt = [];

    /// <summary>
    /// Wait for the user's previous click to finish, then check the limits. Dispose the result after the AI call
    /// (its cost is recorded by then) – until then the user's next click waits.
    /// </summary>
    /// <exception cref="AiLimitExceededException">a limit is reached – nothing was called, nothing changed</exception>
    public async Task<IDisposable> EnterAsync(Guid userId, AiAction action, CancellationToken ct = default)
    {
        var slot = await gate.EnterAsync(userId, ct);
        try
        {
            if (await CheckAsync(userId, action, ct) is { } denial)
            {
                logger.LogInformation("AI limit {Reason} reached by user {UserId}", denial.Reason, userId);
                throw new AiLimitExceededException(denial);
            }
            return slot;
        }
        catch
        {
            slot.Dispose();
            throw;
        }
    }

    /// <summary>"Napsat nový návrh" – at most N new drafts over an existing letter, per job.</summary>
    /// <exception cref="AiLimitExceededException">the job used up its rewrites</exception>
    public async Task EnsureLetterRewriteAllowedAsync(Guid userId, JobMatch match, CancellationToken ct = default)
    {
        if (match.CoverLetter is null || !options.Enabled || await IsExemptAsync(userId, ct))
            return;
        if (match.LetterRewrites >= options.LetterRewritesPerJob)
            throw new AiLimitExceededException(new AiLimitDenial(AiLimitReason.LetterRewrites, options.LetterRewritesPerJob, RetryAt: null));
    }

    /// <returns>null = allowed</returns>
    public async Task<AiLimitDenial?> CheckAsync(Guid userId, AiAction action, CancellationToken ct = default)
    {
        if (!options.Enabled || await IsExemptAsync(userId, ct))
            return null;

        var now = time.GetUtcNow();
        var costs = await ClickCostsAsync(now, ct);

        if (await AppCostAsync(costs, ct) >= options.AppCostPer24hUsd)
        {
            await AlertPausedAsync(now, ct);
            return new AiLimitDenial(AiLimitReason.AppPaused, Limit: null, RetryAt: null);
        }

        if (Usd(costs.GetValueOrDefault(userId)) >= options.UserCostPer24hUsd)
            return new AiLimitDenial(AiLimitReason.UserCost, Limit: null, await CostResetsAtAsync(userId, now, ct));

        var usage = await UsageAsync(userId, action, now, ct);
        return usage.Used >= usage.Limit
            ? new AiLimitDenial(action.Reason(), usage.Limit, usage.ResetsAt)
            : null;
    }

    /// <summary>Everything the UI needs to show what is left.</summary>
    public async Task<AiLimitsView> GetStatusAsync(Guid userId, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var unlimited = !options.Enabled || await IsExemptAsync(userId, ct);
        var costs = await ClickCostsAsync(now, ct);

        return new AiLimitsView(
            unlimited,
            await UsageAsync(userId, AiAction.Profile, now, ct),
            await UsageAsync(userId, AiAction.CoverLetter, now, ct),
            await UsageAsync(userId, AiAction.Evaluation, now, ct),
            await UsageAsync(userId, AiAction.JobImport, now, ct),
            options.LetterRewritesPerJob,
            Usd(costs.GetValueOrDefault(userId)), options.UserCostPer24hUsd, await CostResetsAtAsync(userId, now, ct),
            Paused: !unlimited && await AppCostAsync(costs, ct) >= options.AppCostPer24hUsd);
    }

    // ---------- counting ----------

    /// <summary>Clicks of one kind in the window, oldest first – the oldest one decides when a slot frees up.</summary>
    private async Task<AiLimitUsage> UsageAsync(Guid userId, AiAction action, DateTimeOffset now, CancellationToken ct)
    {
        var purpose = action.Purpose();
        var since = now - AiLimitOptions.Window;
        var times = await db.LlmCalls.AsNoTracking()
            .Where(c => c.UserId == userId && c.ScoutRunId == null && c.Succeeded && c.Purpose == purpose && c.At >= since)
            .OrderBy(c => c.At)
            .Select(c => c.At)
            .ToListAsync(ct);

        var limit = action switch
        {
            AiAction.Profile => options.ProfilePer24h,
            AiAction.CoverLetter => options.LettersPer24h,
            AiAction.JobImport => options.ImportsPer24h,
            _ => options.EvaluationsPer24h,
        };
        // Full: the click that must drop out of the window is the one at [used - limit]; not full: the oldest one
        DateTimeOffset? resets = times.Count == 0 ? null
            : (times.Count >= limit ? times[Math.Max(0, times.Count - limit)] : times[0]) + AiLimitOptions.Window;
        return new AiLimitUsage(times.Count, limit, resets);
    }

    /// <summary>Micro-dollars of clicks per user in the window – one grouped query (a handful of users).</summary>
    private async Task<Dictionary<Guid, long>> ClickCostsAsync(DateTimeOffset now, CancellationToken ct)
    {
        var since = now - AiLimitOptions.Window;
        return await ClickCalls(since)
            .GroupBy(c => c.UserId!.Value)
            .Select(g => new { UserId = g.Key, MicroUsd = g.Sum(c => c.CostMicroUsd) })
            .ToDictionaryAsync(x => x.UserId, x => x.MicroUsd, ct);
    }

    /// <summary>When the user's spending drops: their oldest click in the window leaves it.</summary>
    private async Task<DateTimeOffset?> CostResetsAtAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var oldest = await ClickCalls(now - AiLimitOptions.Window)
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.At)
            .Select(c => c.At)
            .Take(1)
            .ToListAsync(ct);
        return oldest.Count == 0 ? null : oldest[0] + AiLimitOptions.Window;
    }

    /// <summary>The emergency brake counts everybody except the admins – their own experiments must not stop the others.</summary>
    private async Task<decimal> AppCostAsync(Dictionary<Guid, long> costs, CancellationToken ct)
    {
        long total = 0;
        foreach (var (userId, microUsd) in costs)
            if (!await IsExemptAsync(userId, ct))
                total += microUsd;
        return Usd(total);
    }

    /// <summary>A click = a call for a user, outside a morning run, of a kind a button starts.</summary>
    private IQueryable<LlmCall> ClickCalls(DateTimeOffset since) =>
        db.LlmCalls.AsNoTracking().Where(c =>
            c.UserId != null && c.ScoutRunId == null && c.At >= since
            && (c.Purpose == ProfilePurpose || c.Purpose == LetterPurpose || c.Purpose == EvaluationPurpose
                || c.Purpose == ImportPurpose));

    private const string ProfilePurpose = nameof(LlmPurpose.Profile);
    private const string LetterPurpose = nameof(LlmPurpose.CoverLetter);
    private const string EvaluationPurpose = nameof(LlmPurpose.Evaluation);
    private const string ImportPurpose = nameof(LlmPurpose.JobImport);

    private static decimal Usd(long microUsd) => microUsd / 1_000_000m;

    // ---------- who ----------

    private async Task<bool> IsExemptAsync(Guid userId, CancellationToken ct)
    {
        if (admins is null)
            return false;
        if (_exempt.TryGetValue(userId, out var known))
            return known;

        var identity = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Provider, u.Subject })
            .SingleOrDefaultAsync(ct);
        return _exempt[userId] = identity is not null && admins.IsAdmin(identity.Provider, identity.Subject);
    }

    private async Task AlertPausedAsync(DateTimeOffset now, CancellationToken ct)
    {
        if (!gate.ShouldAlertPaused(now))
            return;
        logger.LogWarning("AI on demand paused for everybody: clicks cost {Limit} USD or more in the last 24 hours", options.AppCostPer24hUsd);
        if (notifier is not null)
            await notifier.AiPausedAsync(options.AppCostPer24hUsd, ct);
    }
}
