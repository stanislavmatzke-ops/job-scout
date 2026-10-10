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
