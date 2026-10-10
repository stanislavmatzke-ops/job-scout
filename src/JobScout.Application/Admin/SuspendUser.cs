using JobScout.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Admin;

/// <summary>Stop the AI costs of a user – they still sign in, read their data and see the reason.</summary>
public sealed class SuspendUser(IAppDbContext db, TimeProvider time)
{
    public async Task<bool> ExecuteAsync(Guid userId, string? reason, CancellationToken ct = default)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return false;

        user.Suspend(reason ?? "", time.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return true;
    }
}
