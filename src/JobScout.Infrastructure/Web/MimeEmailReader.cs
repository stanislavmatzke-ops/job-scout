using System.Text;
using JobScout.Application.ManualJobs;
using MimeKit;

namespace JobScout.Infrastructure.Web;

/// <summary>
/// A saved e-mail (.eml) via MimeKit: encodings (base64, quoted-printable), charsets and nested parts are its business.
/// An e-mail forwarded as an attachment is read as part of the text – that is how recruiters' ads often arrive.
/// </summary>
internal sealed class MimeEmailReader : IEmailReader
{
    public EmailMessage Read(byte[] eml)
    {
        MimeMessage message;
        try
        {
            message = MimeMessage.Load(new MemoryStream(eml));
        }
        catch (FormatException ex)
        {
            throw new ArgumentException(NotAnEmail, nameof(eml), ex);
        }
        // The parser accepts almost anything – a real e-mail has at least a sender or a subject
        if (message.From.Count == 0 && string.IsNullOrWhiteSpace(message.Subject))
            throw new ArgumentException(NotAnEmail, nameof(eml));

        var text = new StringBuilder(BodyText(message));
        var attachments = new List<EmailAttachment>();
        // All parts, not only Attachments: a forwarded e-mail is often inline, without "Content-Disposition: attachment"
        foreach (var part in message.BodyParts)
        {
            switch (part)
            {
                case MessagePart { Message: { } forwarded }:
                    text.AppendLine().AppendLine("--- Přeposlaný e-mail ---");
                    if (forwarded.From.Count > 0) text.AppendLine($"Od: {forwarded.From}");
                    if (!string.IsNullOrWhiteSpace(forwarded.Subject)) text.AppendLine($"Předmět: {forwarded.Subject}");
                    text.AppendLine().AppendLine(BodyText(forwarded));
                    break;
                case MimePart { IsAttachment: true, Content: { } content } file:
                    using (var buffer = new MemoryStream())
                    {
                        content.DecodeTo(buffer);
                        attachments.Add(new EmailAttachment(file.FileName ?? "příloha", buffer.ToArray()));
                    }
                    break;
            }
        }

        return new EmailMessage(
            message.Subject,
            message.From.Count > 0 ? message.From.ToString() : null,
            message.Date == DateTimeOffset.MinValue ? null : message.Date,
            text.ToString().Trim(),
            attachments);
    }

    /// <summary>The plain-text body when there is one; otherwise the HTML one turned into text.</summary>
    private static string BodyText(MimeMessage message) =>
        message.TextBody is { Length: > 0 } plain ? plain
        : message.HtmlBody is { Length: > 0 } html ? HtmlText.Extract(html).Text
        : "";

    private const string NotAnEmail = "Soubor .eml nejde přečíst jako e-mail. Uložte ho prosím znovu, nebo zkopírujte jeho text.";
}
