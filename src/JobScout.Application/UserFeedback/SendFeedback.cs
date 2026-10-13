using JobScout.Application.Abstractions;
using JobScout.Application.Demo;
using JobScout.Application.Notifications;
using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.UserFeedback;

public enum SendFeedbackResult { Sent, TooMany }

/// <summary>
/// "Zpětná vazba": stored, then e-mailed to the admins. A few messages a day per user is plenty – the limit only stops
/// a script from flooding the admins' inbox. The shared demo account gets more (many visitors share it).
/// </summary>
public sealed class SendFeedback(IAppDbContext db, TimeProvider time, Notifier? notifier = null)
{
    public const int PerUserPer24h = 5;
    public const int DemoPer24h = 30;

    /// <exception cref="ArgumentException">empty or too long message, rating outside 1–5</exception>
    public async Task<SendFeedbackResult> ExecuteAsync(
        Guid userId, FeedbackKind kind, int? rating, string? message, bool replyWanted, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);

        var since = now.AddHours(-24);
        var recent = await db.Feedback.CountAsync(f => f.UserId == userId && f.CreatedAt >= since, ct);
        if (recent >= (user.Provider == DemoAccount.Provider ? DemoPer24h : PerUserPer24h))
            return SendFeedbackResult.TooMany;

        // The demo account has no mailbox – nobody to answer to
        var feedback = new Feedback(userId, user.DisplayName, kind, rating, message ?? "",
            replyWanted && !string.IsNullOrWhiteSpace(user.Email), now);
        db.Feedback.Add(feedback);
        await db.SaveChangesAsync(ct);

        if (notifier is not null)
            await notifier.FeedbackAsync(user, feedback, ct);
        return SendFeedbackResult.Sent;
    }
}
