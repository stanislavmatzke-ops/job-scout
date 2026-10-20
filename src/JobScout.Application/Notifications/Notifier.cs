using JobScout.Application.Abstractions;
using JobScout.Application.Demo;
using JobScout.Application.Reports;
using JobScout.Application.Users;
using JobScout.Core.Domain;
using Microsoft.Extensions.Logging;

namespace JobScout.Application.Notifications;

/// <summary>
/// Sends the e-mails of 7f after the change is saved. A failed e-mail is only logged – it never undoes an approval
/// or a morning run. Test personas and the demo account have no mailbox and get nothing.
/// </summary>
public sealed class Notifier(IEmailSender sender, NotificationOptions options, ILogger<Notifier> logger)
{
    public async Task AccessRequestedAsync(User user, CancellationToken ct = default)
    {
        foreach (var admin in AdminAddresses())
            await SendAsync(Emails.AccessRequested(admin, user, options.AppUrl), ct);
    }

    /// <summary>A user wrote in "Zpětná vazba" – every admin gets it; Reply-To is the user when they want an answer.</summary>
    public async Task FeedbackAsync(User user, Feedback feedback, CancellationToken ct = default)
    {
        foreach (var admin in AdminAddresses())
            await SendAsync(Emails.FeedbackReceived(admin, user, feedback, options.AppUrl), ct);
    }

    /// <summary>The app-wide brake on AI clicks engaged (ADR-0021) – the admins should look at who spends what.</summary>
    public async Task AiPausedAsync(decimal limitUsd, CancellationToken ct = default)
    {
        foreach (var admin in AdminAddresses())
            await SendAsync(Emails.AiPaused(admin, limitUsd, options.AppUrl), ct);
    }

    /// <summary>The morning run failed for some users (a source down, a bot check…) – otherwise nobody would notice.</summary>
    public async Task MorningRunFailedAsync(int failed, int total, string firstError, CancellationToken ct = default)
    {
        foreach (var admin in AdminAddresses())
            await SendAsync(Emails.MorningRunFailed(admin, failed, total, firstError, options.AppUrl), ct);
    }

    public Task ApprovedAsync(User user, CancellationToken ct = default) =>
        Reachable(user) ? SendAsync(Emails.Approved(user, options.AppUrl), ct) : Task.CompletedTask;

    public Task RejectedAsync(User user, CancellationToken ct = default) =>
        Reachable(user) ? SendAsync(Emails.Rejected(user, options.AppUrl), ct) : Task.CompletedTask;

    public Task SuspendedAsync(User user, CancellationToken ct = default) =>
        Reachable(user) ? SendAsync(Emails.Suspended(user, options.AppUrl), ct) : Task.CompletedTask;

    /// <returns>true = an e-mail went out (there was something new and the user wants it)</returns>
    public async Task<bool> MorningAsync(User user, MorningReport report, CancellationToken ct = default)
    {
        if (!Reachable(user) || user.MorningEmailOff)
            return false;
        var message = Emails.Morning(user, report, options.AppUrl);
        if (message is null)
            return false;
        await SendAsync(message, ct);
        return true;
    }

    private IEnumerable<string> AdminAddresses() => options.AdminAddresses.Where(a => !string.IsNullOrWhiteSpace(a));

    private static bool Reachable(User user) =>
        !string.IsNullOrWhiteSpace(user.Email) && user.Provider is not (SignInUser.DevProvider or DemoAccount.Provider);

    private async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        try
        {
            await sender.SendAsync(message, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "E-mail „{Subject}“ se nepodařilo odeslat", message.Subject);
        }
    }
}
