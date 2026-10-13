using Azure;
using Azure.Communication.Email;
using JobScout.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace JobScout.Infrastructure.Email;

/// <summary>
/// E-mails through Azure Communication Services (7f). WaitUntil.Started: ACS queues the message and we don't wait
/// for the delivery – the user is not kept waiting behind a mail server.
/// </summary>
internal sealed class AcsEmailSender(EmailClient client, string from, ILogger<AcsEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(Application.Abstractions.EmailMessage message, CancellationToken ct = default)
    {
        var mail = new Azure.Communication.Email.EmailMessage(from, message.To,
            new EmailContent(message.Subject) { PlainText = message.Text, Html = message.Html });
        if (!string.IsNullOrWhiteSpace(message.ReplyTo))
            mail.ReplyTo.Add(new EmailAddress(message.ReplyTo));
        await client.SendAsync(WaitUntil.Started, mail, ct);
        logger.LogInformation("E-mail „{Subject}“ odeslán na {To}", message.Subject, LogEmailSender.Mask(message.To));
    }
}

/// <summary>Locally and in tests: no e-mail leaves the machine, the log says what would have been sent.</summary>
internal sealed class LogEmailSender(ILogger<LogEmailSender> logger) : IEmailSender
{
    public Task SendAsync(Application.Abstractions.EmailMessage message, CancellationToken ct = default)
    {
        logger.LogInformation("E-mail (neodeslán – Email:ConnectionString není nastaven): „{Subject}“ pro {To}",
            message.Subject, Mask(message.To));
        return Task.CompletedTask;
    }

    /// <summary>"jana.novakova@example.com" → "j***@example.com" – no full addresses in the logs.</summary>
    internal static string Mask(string address)
    {
        var at = address.IndexOf('@');
        return at <= 0 ? "***" : $"{address[0]}***{address[at..]}";
    }
}
