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

    /// <summary>The morning e-mail with new jobs – on by default, the user can turn it off (7f).</summary>
    public async Task SetMorningEmailAsync(Guid userId, bool on, CancellationToken ct = default)
    {
        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);
        user.SetMorningEmail(on);
        await db.SaveChangesAsync(ct);
    }
}
