using JobScout.Application.Abstractions;
using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Admin;

public sealed record AdminUserRow(
    Guid Id, string Provider, string DisplayName, string Email, AccessStatus Access,
    DateTimeOffset CreatedAt, DateTimeOffset? LastSignInAt,
    DateTimeOffset? AccessRequestedAt, string? AccessRequestMessage, string? SuspendedReason,
    bool HasProfile, decimal CostLast7DaysUsd, decimal CostLast30DaysUsd,
    DateTimeOffset? RejectedAt = null, string? RejectedReason = null,
    decimal ClickCostLast24hUsd = 0);               // AI started by clicking (profile, evaluation, letters) – ADR-0021

/// <summary>Admin overview: who waits for a decision, who costs what (ADR-0016 cost records).</summary>
public sealed class ListUsers(IAppDbContext db, TimeProvider time)
{
    public async Task<IReadOnlyList<AdminUserRow>> ExecuteAsync(CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var users = await db.Users.AsNoTracking().ToListAsync(ct);
        var withProfile = (await db.UserProfiles.Select(p => p.UserId).Distinct().ToListAsync(ct)).ToHashSet();
        var cost7 = await CostSinceAsync(now.AddDays(-7), ct);
        var cost30 = await CostSinceAsync(now.AddDays(-30), ct);
        var clicks24 = await CostSinceAsync(now.AddHours(-24), ct, clicksOnly: true);

        return users
            .Select(u => new AdminUserRow(
                u.Id, u.Provider, u.DisplayName, u.Email, u.Access, u.CreatedAt, u.LastSignInAt,
                u.AccessRequestedAt, u.AccessRequestMessage, u.SuspendedReason,
                withProfile.Contains(u.Id), Usd(cost7, u.Id), Usd(cost30, u.Id), u.RejectedAt, u.RejectedReason,
                Usd(clicks24, u.Id)))
            .OrderByDescending(r => r.Access == AccessStatus.Pending && r.AccessRequestedAt != null)   // waiting for me
            .ThenByDescending(r => r.CostLast30DaysUsd)                                               // then the expensive ones
            .ThenByDescending(r => r.CreatedAt)
            .ToList();
    }

    /// <summary>One grouped query per window – micro-dollars sum exactly in SQL.</summary>
    /// <param name="clicksOnly">only what the user started by clicking: no morning run, a kind a button starts (ADR-0021)</param>
    private async Task<Dictionary<Guid, long>> CostSinceAsync(DateTimeOffset since, CancellationToken ct, bool clicksOnly = false) =>
        await db.LlmCalls
            .Where(c => c.UserId != null && c.At >= since)
            .Where(c => !clicksOnly || (c.ScoutRunId == null
                && (c.Purpose == ProfilePurpose || c.Purpose == LetterPurpose || c.Purpose == EvaluationPurpose)))
            .GroupBy(c => c.UserId!.Value)
            .Select(g => new { UserId = g.Key, MicroUsd = g.Sum(c => c.CostMicroUsd) })
            .ToDictionaryAsync(x => x.UserId, x => x.MicroUsd, ct);

    private const string ProfilePurpose = nameof(Llm.LlmPurpose.Profile);
    private const string LetterPurpose = nameof(Llm.LlmPurpose.CoverLetter);
    private const string EvaluationPurpose = nameof(Llm.LlmPurpose.Evaluation);

    private static decimal Usd(Dictionary<Guid, long> costs, Guid userId) => costs.GetValueOrDefault(userId) / 1_000_000m;
}
