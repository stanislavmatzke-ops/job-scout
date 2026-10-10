using JobScout.Application.Abstractions;
using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Users;

/// <summary>"Požádat o přístup" – Pending or Suspended users ask the admin (ADR-0017).</summary>
public sealed class RequestAccess(IAppDbContext db, TimeProvider time)
{
    /// <returns>false = already approved, nothing to ask for</returns>
    public async Task<bool> ExecuteAsync(Guid userId, string? message, CancellationToken ct = default)
    {
        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);
        if (user.Access == AccessStatus.Approved)
            return false;

        user.RequestAccess(message, time.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return true;
    }
}
