using JobScout.Application.Abstractions;
using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Profiles;

public sealed class UpdateProfileSettings(IAppDbContext db)
{
    public async Task ExecuteAsync(Guid userId, GrammaticalGender gender, CancellationToken ct = default)
    {
        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);
        user.SetGender(gender);
        await db.SaveChangesAsync(ct);
    }
}
