using JobScout.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Admin;

/// <summary>UsedBy = display name of whoever signed in with it (null = not used yet). The code itself is never known again.</summary>
public sealed record InvitationRow(
    Guid Id, string Note, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? UsedAt, string? UsedBy, bool IsUsable);

/// <summary>Admin overview of invitations (ADR-0011) – which ones were used, by whom, and which still wait.</summary>
public sealed class ListInvitations(IAppDbContext db, TimeProvider time)
{
    public const int Limit = 50;

    public async Task<IReadOnlyList<InvitationRow>> ExecuteAsync(CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var invitations = await db.Invitations.AsNoTracking().ToListAsync(ct);
        var userIds = invitations.Where(i => i.UsedByUserId != null).Select(i => i.UsedByUserId!.Value).ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        return invitations
            .OrderByDescending(i => i.CreatedAt)          // in memory – DateTimeOffset is stored as binary in SQLite
            .Take(Limit)
            .Select(i => new InvitationRow(
                i.Id, i.Note, i.CreatedAt, i.ExpiresAt, i.UsedAt,
                i.UsedByUserId is { } id ? names.GetValueOrDefault(id, "(smazaný uživatel)") : null,
                i.IsUsable(now)))
            .ToList();
    }
}
