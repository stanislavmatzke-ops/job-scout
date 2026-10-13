using JobScout.Application.Abstractions;
using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.UserFeedback;

public sealed record FeedbackRow(
    Guid Id, Guid UserId, string UserName, string? Email, FeedbackKind Kind, int? Rating, string Message,
    bool ReplyWanted, DateTimeOffset CreatedAt);

/// <summary>Admin: the newest feedback first (the e-mail is the notification, this is the archive).</summary>
public sealed class ListFeedback(IAppDbContext db)
{
    public const int Max = 200;

    public async Task<IReadOnlyList<FeedbackRow>> ExecuteAsync(CancellationToken ct = default)
    {
        var rows = await db.Feedback.AsNoTracking()
            .OrderByDescending(f => f.CreatedAt)
            .Take(Max)
            .ToListAsync(ct);

        var userIds = rows.Select(f => f.UserId).Distinct().ToList();
        var emails = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Email })
            .ToDictionaryAsync(u => u.Id, u => u.Email, ct);

        return rows
            .Select(f => new FeedbackRow(f.Id, f.UserId, f.UserName,
                emails.TryGetValue(f.UserId, out var email) && !string.IsNullOrWhiteSpace(email) ? email : null,
                f.Kind, f.Rating, f.Message, f.ReplyWanted, f.CreatedAt))
            .ToList();
    }
}
