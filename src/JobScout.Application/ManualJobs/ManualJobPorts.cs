using JobScout.Application.Llm;

namespace JobScout.Application.ManualJobs;

/// <summary>A web page the user pasted a link to: its text (HTML, plain text) or the document itself (a PDF).</summary>
/// <param name="Url">Where the page really was – after redirects.</param>
public sealed record WebPage(Uri Url, string? Title, string? Text, LlmDocument? Document);

/// <summary>
/// Downloads one page the user asked for (ADR-0013) – any public web address, so the adapter must refuse addresses
/// inside our own network (SSRF), keep the size small and give up quickly.
/// </summary>
public interface IWebPageReader
{
    /// <exception cref="WebPageUnavailableException">not reachable, refused, too big, or not a page we can read</exception>
    Task<WebPage> ReadAsync(Uri url, CancellationToken ct = default);
}

/// <summary>The page could not be read – the message is for the user (Czech, like every API message).</summary>
public sealed class WebPageUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>A file attached to an e-mail.</summary>
public sealed record EmailAttachment(string FileName, byte[] Content);

/// <summary>A saved e-mail (.eml) taken apart: the headers that matter, the body as plain text, the attachments.</summary>
public sealed record EmailMessage(string? Subject, string? From, DateTimeOffset? Date, string Text, IReadOnlyList<EmailAttachment> Attachments);

/// <summary>Reads a saved e-mail (MIME, RFC 5322) – encodings, charsets and attachments are the adapter's business.</summary>
public interface IEmailReader
{
    /// <exception cref="ArgumentException">not an e-mail we can read</exception>
    EmailMessage Read(byte[] eml);
}
