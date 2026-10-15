using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using JobScout.Application.Llm;
using JobScout.Application.ManualJobs;
using Microsoft.Extensions.Logging;

namespace JobScout.Infrastructure.Web;

/// <summary>
/// Reads one page the user pasted a link to (ADR-0013). The address comes from a user, so the server must not become
/// their tool for reaching inside our network (SSRF): the HttpClient connects only to public addresses on ports 80/443
/// (<see cref="SafeHandler"/>), also after redirects. One plain request – no browser: a page that only renders in
/// JavaScript gives too little text, and the user is asked to paste the text instead.
/// </summary>
internal sealed class WebPageReader(HttpClient http, ILogger<WebPageReader> logger) : IWebPageReader
{
    /// <summary>A job ad page is a few hundred kB; a PDF ad a few MB at most.</summary>
    public const int MaxBytes = 5 * 1024 * 1024;

    /// <summary>Less text than this = an empty shell rendered by JavaScript, a cookie wall, a login page…</summary>
    public const int MinTextChars = 200;

    public async Task<WebPage> ReadAsync(Uri url, CancellationToken ct = default)
    {
        if (url.Scheme is not ("http" or "https") || !url.IsDefaultPort)
            throw new WebPageUnavailableException(Refused);

        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                throw new WebPageUnavailableException(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone
                    ? "Stránka na zadané adrese neexistuje – inzerát byl možná stažen."
                    : $"Stránka odpověděla chybou ({(int)response.StatusCode}). Zkopírujte prosím text inzerátu a vložte ho jako text.");

            var finalUrl = response.RequestMessage?.RequestUri ?? url;
            var body = await ReadBoundedAsync(response.Content, ct);
            var page = Parse(finalUrl, response.Content.Headers.ContentType, body);
            logger.LogInformation("Stránka {Host}: {Chars} znaků textu{Pdf}", finalUrl.Host, page.Text?.Length ?? 0,
                page.Document is null ? "" : ", PDF");
            return page;
        }
        catch (HttpRequestException ex)
        {
            throw new WebPageUnavailableException(
                ex.InnerException is BlockedAddressException ? Refused : "Stránku se nepodařilo načíst – zkontrolujte prosím adresu.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new WebPageUnavailableException("Stránka neodpověděla včas. Zkuste to prosím znovu, nebo vložte text inzerátu.", ex);
        }
    }

    private static WebPage Parse(Uri url, MediaTypeHeaderValue? contentType, byte[] body)
    {
        var media = contentType?.MediaType?.ToLowerInvariant();
        if (media == "application/pdf" || (media is null or "application/octet-stream" && body.AsSpan().StartsWith("%PDF-"u8)))
        {
            if (!body.AsSpan().StartsWith("%PDF-"u8))
                throw new WebPageUnavailableException(Unsupported);
            return new WebPage(url, null, null, new LlmDocument("Inzerát (PDF z webu)", "application/pdf", body));
        }

        string? title;
        string text;
        if (media is "text/html" or "application/xhtml+xml" or null)
            (title, text) = HtmlText.Extract(new MemoryStream(body));    // AngleSharp finds the charset (header or <meta>)
        else if (media == "text/plain")
            (title, text) = (null, Decode(body, contentType?.CharSet));
        else
            throw new WebPageUnavailableException(Unsupported);

        if (text.Length < MinTextChars)
            throw new WebPageUnavailableException(
                "Ze stránky se nepodařilo přečíst text inzerátu – nejspíš se načítá až v prohlížeči nebo vyžaduje přihlášení. " +
                "Zkopírujte prosím text inzerátu a vložte ho jako text.");
        return new WebPage(url, title, text, null);
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken ct)
    {
        if (content.Headers.ContentLength > MaxBytes)
            throw new WebPageUnavailableException(TooBig);

        await using var stream = await content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
                throw new WebPageUnavailableException(TooBig);     // Content-Length may lie or be missing
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static string Decode(byte[] body, string? charset)
    {
        try
        {
            return (charset is null ? System.Text.Encoding.UTF8 : System.Text.Encoding.GetEncoding(charset)).GetString(body);
        }
        catch (ArgumentException)
        {
            return System.Text.Encoding.UTF8.GetString(body);
        }
    }

    private const string Refused = "Tuto adresu nelze načíst. Zadejte prosím veřejnou webovou stránku s inzerátem.";
    private const string TooBig = "Stránka je příliš velká. Zkopírujte prosím text inzerátu a vložte ho jako text.";
    private const string Unsupported = "Na adrese není webová stránka ani PDF. Stáhněte prosím soubor a nahrajte ho, nebo vložte text inzerátu.";

    // ---------- SSRF protection ----------

    /// <summary>
    /// The primary handler of this HttpClient: resolves the name itself and connects only to a public address on 80/443 –
    /// checked at connect time, so neither a redirect nor a DNS answer that changes after a check can lead inside.
    /// No proxy (it would connect for us and skip the check).
    /// </summary>
    public static SocketsHttpHandler SafeHandler() => new()
    {
        UseProxy = false,
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5,
        AutomaticDecompression = DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        ConnectCallback = async (context, ct) =>
        {
            var target = context.DnsEndPoint;
            if (target.Port is not (80 or 443))
                throw new BlockedAddressException();

            var addresses = IPAddress.TryParse(target.Host, out var literal)
                ? [literal]
                : await Dns.GetHostAddressesAsync(target.Host, ct);
            // Every address must be public – a name that also points inside is refused as a whole
            if (addresses.Length == 0 || !addresses.All(IsPublic))
                throw new BlockedAddressException();

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(addresses, target.Port, ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };

    /// <summary>Public unicast only: no loopback, private, link-local (cloud metadata 169.254.169.254), CGNAT, multicast, reserved.</summary>
    internal static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        else if (address.AddressFamily == AddressFamily.InterNetworkV6 && IsNat64(address.GetAddressBytes()))
            address = new IPAddress(address.GetAddressBytes()[12..]);    // 64:ff9b::/96 carries an IPv4 address – judge that one

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(b[0] == 0                                     // "this network"
                     || b[0] == 10                                 // private
                     || b[0] == 127                                // loopback
                     || (b[0] == 100 && b[1] >= 64 && b[1] <= 127) // CGNAT 100.64/10
                     || (b[0] == 169 && b[1] == 254)               // link-local, cloud metadata
                     || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)  // private
                     || (b[0] == 192 && b[1] == 0 && b[2] == 0)    // IETF protocol assignments
                     || (b[0] == 192 && b[1] == 168)               // private
                     || (b[0] == 198 && b[1] is 18 or 19)          // benchmarking
                     || b[0] >= 224);                              // multicast, reserved, broadcast
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = address.GetAddressBytes();
            return !(IPAddress.IPv6Loopback.Equals(address) || IPAddress.IPv6Any.Equals(address)
                     || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast
                     || (b[0] & 0xFE) == 0xFC                       // unique local fc00::/7
                     || address.IsIPv6Teredo || (b[0] == 0x20 && b[1] == 0x02));   // tunnels to IPv4 (2002::/16)
        }

        return false;
    }

    private static bool IsNat64(byte[] b) =>
        b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xff && b[3] == 0x9b && b[4..12].All(x => x == 0);

    /// <summary>Thrown inside the connect callback; HttpClient wraps it into an HttpRequestException.</summary>
    private sealed class BlockedAddressException() : Exception("The address is not a public web address.");
}
