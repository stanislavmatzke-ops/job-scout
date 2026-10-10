using JobScout.Application.Abstractions;
using JobScout.Core.Domain;

namespace JobScout.Application.Users;

public sealed record CreatedInvitation(Guid Id, string Code, DateTimeOffset ExpiresAt);

public sealed class CreateInvitation(IAppDbContext db, TimeProvider time)
{
    public async Task<CreatedInvitation> ExecuteAsync(string note, CancellationToken ct = default)
    {
        var (invitation, code) = Invitation.Create(note, time.GetUtcNow());
        db.Invitations.Add(invitation);
        await db.SaveChangesAsync(ct);
        return new CreatedInvitation(invitation.Id, code, invitation.ExpiresAt);
    }
}
