using JobScout.Application.Abstractions;
using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Llm;

public sealed record UsageLine(string Purpose, int Calls, int Failed, long InputTokens, long OutputTokens, long CostMicroUsd);

public sealed record UsageSummary(IReadOnlyList<UsageLine> Lines)
{
    public long TotalMicroUsd => Lines.Sum(l => l.CostMicroUsd);
    public decimal TotalUsd => TotalMicroUsd / 1_000_000m;
}

/// <summary>What did a run / a user cost – grouped by purpose, summed by the database.</summary>
public sealed class UsageReport(IAppDbContext db)
{
    public Task<UsageSummary> ForRunAsync(Guid scoutRunId, CancellationToken ct = default) =>
        SummarizeAsync(db.LlmCalls.Where(c => c.ScoutRunId == scoutRunId), ct);

    public Task<UsageSummary> ForUserAsync(Guid userId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default) =>
        SummarizeAsync(db.LlmCalls.Where(c => c.UserId == userId && c.At >= from && c.At < to), ct);

    private static async Task<UsageSummary> SummarizeAsync(IQueryable<LlmCall> calls, CancellationToken ct)
    {
        var lines = await calls
            .GroupBy(c => c.Purpose)
            .Select(g => new UsageLine(
                g.Key, g.Count(), g.Count(c => !c.Succeeded),
                g.Sum(c => c.InputTokens), g.Sum(c => c.OutputTokens), g.Sum(c => c.CostMicroUsd)))
            .ToListAsync(ct);

        return new UsageSummary(lines.OrderByDescending(l => l.CostMicroUsd).ToList());
    }
}