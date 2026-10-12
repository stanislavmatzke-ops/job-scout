using JobScout.Application.Abstractions;
using JobScout.Application.Notifications;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Admin;

public sealed class ApproveUser(IAppDbContext db, TimeProvider time, Notifier? notifier = null)
{
    public async Task<bool> ExecuteAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return false;

        var wasApproved = user.Access == JobScout.Core.Domain.AccessStatus.Approved;
        user.Approve(time.GetUtcNow());
        await db.SaveChangesAsync(ct);
        if (!wasApproved && notifier is not null) await notifier.ApprovedAsync(user, ct);   // "Přístup schválen" (7f)
        return true;
    }
}
