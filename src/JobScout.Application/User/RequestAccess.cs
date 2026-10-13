using JobScout.Application.Abstractions;
using JobScout.Application.Notifications;
using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Users;

/// <summary>
/// "Požádat o přístup" – Pending or Suspended users ask the admin (ADR-0017). Anyone with a Microsoft or Google account
/// can ask, so the admin is e-mailed at most once a day per user – asking again only updates the message (13 Oct).
/// </summary>
public sealed class RequestAccess(IAppDbContext db, TimeProvider time, Notifier? notifier = null)
{
    public static readonly TimeSpan NotifyAtMostEvery = TimeSpan.FromHours(24);

    /// <returns>false = already approved, nothing to ask for</returns>
    public async Task<bool> ExecuteAsync(Guid userId, string? message, CancellationToken ct = default)
    {
        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);
        if (user.Access == AccessStatus.Approved)
            return false;

        var now = time.GetUtcNow();
        // A new question after a rejection is news; the same request again within a day is not
        var notify = user.RejectedAt is not null || user.AccessRequestedAt is not { } asked || now - asked >= NotifyAtMostEvery;

        user.RequestAccess(message, now);
        await db.SaveChangesAsync(ct);
        if (notify && notifier is not null) await notifier.AccessRequestedAsync(user, ct);     // the admin hears about it (7f)
        return true;
    }
}
