namespace JobScout.Application.Abstractions;

/// <summary>One e-mail: plain text for every client, HTML for the nice ones. ReplyTo = where "Reply" goes (feedback → the user).</summary>
public sealed record EmailMessage(string To, string Subject, string Text, string Html, string? ReplyTo = null);

/// <summary>
/// Sending e-mails (7f) – Azure Communication Services in the cloud, only a log line locally.
/// Fire and forget from the use case's point of view: a failed e-mail never undoes an approval.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}
