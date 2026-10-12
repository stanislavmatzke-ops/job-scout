using JobScout.Application.Abstractions;
using JobScout.Application.Notifications;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Admin;

public enum RejectResult { Rejected, NotFound, NotPending }

/// <summary>Turn down an access request – the user stays outside the app, sees the reason, may ask again.</summary>
public sealed class RejectUser(IAppDbContext db, TimeProvider time, Notifier? notifier = null)
{
    public async Task<RejectResult> ExecuteAsync(Guid userId, string? reason, CancellationToken ct = default)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return RejectResult.NotFound;
        if (user.Access != JobScout.Core.Domain.AccessStatus.Pending)
            return RejectResult.NotPending;

        user.Reject(reason ?? "", time.GetUtcNow());
        await db.SaveChangesAsync(ct);
        if (notifier is not null) await notifier.RejectedAsync(user, ct);
        return RejectResult.Rejected;
    }
}
