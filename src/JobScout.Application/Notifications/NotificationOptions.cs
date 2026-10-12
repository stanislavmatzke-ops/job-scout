namespace JobScout.Application.Notifications;

/// <summary>Configuration "Email" (7f).</summary>
public sealed class NotificationOptions
{
    public const string SectionName = "Email";

    /// <summary>The app's public address for links in e-mails, e.g. https://jobscout.stanislavmatzke.com</summary>
    public string AppUrl { get; init; } = "https://localhost:4200";

    /// <summary>Who hears about new access requests.</summary>
    public List<string> AdminAddresses { get; init; } = [];
}
