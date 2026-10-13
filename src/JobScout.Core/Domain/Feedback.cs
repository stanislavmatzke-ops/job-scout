namespace JobScout.Core.Domain;

/// <summary>What the feedback is about – the user picks one, the admin sorts by it.</summary>
public enum FeedbackKind { Idea, Problem, Praise, Other }

/// <summary>
/// A message from a user to the author ("Zpětná vazba"): stored here and sent to the admins by e-mail.
/// No foreign key on purpose – the feedback stays even if the account is deleted (the name is copied in).
/// </summary>
public sealed class Feedback
{
    public const int MaxMessageLength = 4000;

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string UserName { get; private set; } = "";
    public FeedbackKind Kind { get; private set; }
    /// <summary>1–5 "how do you like Job Scout", optional.</summary>
    public int? Rating { get; private set; }
    public string Message { get; private set; } = "";
    /// <summary>The user wants an answer to their e-mail address.</summary>
    public bool ReplyWanted { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Feedback() { }   // for EF Core

    public Feedback(Guid userId, string userName, FeedbackKind kind, int? rating, string message, bool replyWanted, DateTimeOffset now)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown feedback kind.");
        if (rating is < 1 or > 5)
            throw new ArgumentException("Hodnocení musí být 1 až 5 hvězdiček.", nameof(rating));
        message = message?.Trim() ?? "";
        if (message.Length == 0)
            throw new ArgumentException("Napište prosím, co nám chcete sdělit.", nameof(message));
        if (message.Length > MaxMessageLength)
            throw new ArgumentException($"Zpráva může mít nejvýš {MaxMessageLength} znaků.", nameof(message));

        Id = Guid.CreateVersion7(now);
        UserId = userId;
        UserName = userName;
        Kind = kind;
        Rating = rating;
        Message = message;
        ReplyWanted = replyWanted;
        CreatedAt = now;
    }
}
