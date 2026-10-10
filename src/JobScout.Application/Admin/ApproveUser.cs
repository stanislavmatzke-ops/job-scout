using JobScout.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Admin;

public sealed class ApproveUser(IAppDbContext db, TimeProvider time)
{
    public async Task<bool> ExecuteAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return false;

        user.Approve(time.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return true;                     // phase 7: the "Přístup schválen" e-mail goes out here
    }
}
