namespace JobScout.Core.Domain;

/// <summary>Signed-in person. Identity = Provider + Subject from the SSO token (never the e-mail).</summary>
public sealed class User
{
    public Guid Id { get; private set; }
    public string Provider { get; private set; } = "";   // "microsoft" | "google"
    public string Subject { get; private set; } = "";    // stable user id from the identity provider
    public string Email { get; private set; } = "";
    public string DisplayName { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public GrammaticalGender Gender { get; private set; }
    public const int MaxRequestMessageLength = 1000;
    public const int MaxSuspendReasonLength = 500;

    public AccessStatus Access { get; private set; } = AccessStatus.Pending;
    public DateTimeOffset? AccessRequestedAt { get; private set; }
    public string? AccessRequestMessage { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public DateTimeOffset? SuspendedAt { get; private set; }
    public string? SuspendedReason { get; private set; }
    public DateTimeOffset? LastSignInAt { get; private set; }

    public void RecordSignIn(DateTimeOffset now) => LastSignInAt = now;

    public void RequestAccess(string? message, DateTimeOffset now)
    {
        if (Access == AccessStatus.Approved)
            throw new InvalidOperationException("Uživatel už přístup má.");

        message = string.IsNullOrWhiteSpace(message) ? null : message.Trim();
        if (message?.Length > MaxRequestMessageLength)
            throw new ArgumentException($"Zpráva může mít nejvýš {MaxRequestMessageLength} znaků.", nameof(message));

        AccessRequestedAt = now;
        AccessRequestMessage = message;
    }

    public void Approve(DateTimeOffset now)
    {
        Access = AccessStatus.Approved;
        ApprovedAt = now;
        SuspendedAt = null;
        SuspendedReason = null;
    }

    public void Suspend(string reason, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Uživatel uvidí důvod pozastavení – nesmí chybět.", nameof(reason));
        reason = reason.Trim();
        if (reason.Length > MaxSuspendReasonLength)
            throw new ArgumentException($"Důvod může mít nejvýš {MaxSuspendReasonLength} znaků.", nameof(reason));

        Access = AccessStatus.Suspended;
        SuspendedAt = now;
        SuspendedReason = reason;
        AccessRequestedAt = null;          // the old request is settled – asking again is a new request
        AccessRequestMessage = null;
    }
    private User() { }   // for EF Core

    public User(string provider, string subject, string email, string displayName, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        Id = Guid.CreateVersion7(now);
        Provider = provider;
        Subject = subject;
        Email = email;
        DisplayName = displayName;
        CreatedAt = now;
    }

    /// <summary>E-mail and name can change at the provider – refresh them on sign-in.</summary>
    public void UpdateContact(string email, string displayName)
    {
        Email = email;
        DisplayName = displayName;
    }

    public void SetGender(GrammaticalGender gender)
    {
        if (!Enum.IsDefined(gender))
            throw new ArgumentOutOfRangeException(nameof(gender), gender, "Neznámý rod.");
        Gender = gender;
    }
}
