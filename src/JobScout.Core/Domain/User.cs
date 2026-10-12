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
    /// <summary>The last request was turned down (stays Pending – no access at all; may ask again).</summary>
    public DateTimeOffset? RejectedAt { get; private set; }
    public string? RejectedReason { get; private set; }
    public DateTimeOffset? LastSignInAt { get; private set; }
    /// <summary>The user turned the morning e-mail off (default: on – stored as "off" so existing rows need no default).</summary>
    public bool MorningEmailOff { get; private set; }

    public void SetMorningEmail(bool on) => MorningEmailOff = !on;

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
        RejectedAt = null;                 // a new request – the old answer no longer applies
        RejectedReason = null;
    }

    public void Approve(DateTimeOffset now)
    {
        Access = AccessStatus.Approved;
        ApprovedAt = now;
        SuspendedAt = null;
        SuspendedReason = null;
        RejectedAt = null;
        RejectedReason = null;
    }

    /// <summary>
    /// "No" to someone who never had access: they stay Pending (nothing in the app opens), see the reason
    /// and may ask again. Different from Suspend, which is for members – they keep reading their data.
    /// </summary>
    public void Reject(string reason, DateTimeOffset now)
    {
        if (Access != AccessStatus.Pending)
            throw new InvalidOperationException("Zamítnout lze jen uživatele, který přístup ještě nemá – člena lze pozastavit.");

        RejectedReason = ValidReason(reason);
        RejectedAt = now;
        AccessRequestedAt = null;          // the request is settled – asking again is a new request
        AccessRequestMessage = null;
    }

    private static string ValidReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Uživatel uvidí důvod – nesmí chybět.", nameof(reason));
        reason = reason.Trim();
        if (reason.Length > MaxSuspendReasonLength)
            throw new ArgumentException($"Důvod může mít nejvýš {MaxSuspendReasonLength} znaků.", nameof(reason));
        return reason;
    }

    public void Suspend(string reason, DateTimeOffset now)
    {
        reason = ValidReason(reason);

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
