using JobScout.Application.Abstractions;

namespace JobScout.Tests.TestUtils;

/// <summary>Remembers every e-mail instead of sending it; can be told to fail like an unreachable mail service.</summary>
internal sealed class FakeEmailSender : IEmailSender
{
    public List<EmailMessage> Sent { get; } = [];
    public bool Fail { get; set; }

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (Fail) throw new InvalidOperationException("Mail service down");
        Sent.Add(message);
        return Task.CompletedTask;
    }
}
