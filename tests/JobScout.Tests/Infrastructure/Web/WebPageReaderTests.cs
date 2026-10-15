using System.Net;
using System.Text;
using JobScout.Application.ManualJobs;
using JobScout.Infrastructure.Web;
using JobScout.Tests.TestUtils;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobScout.Tests.Infrastructure.Web;

/// <summary>
/// A page the user pasted a link to (ADR-0013): its readable text or the PDF – and never an address inside our network.
/// </summary>
public sealed class WebPageReaderTests
{
    private static readonly string LongText = string.Join(' ', Enumerable.Repeat("Hledáme zkušeného stavbyvedoucího.", 10));

    private static WebPageReader Reader(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new StubHttpHandler(respond)), NullLogger<WebPageReader>.Instance);

    private static HttpResponseMessage Bytes(HttpRequestMessage request, byte[] body, string? mediaType) =>
        new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(body) { Headers = { ContentType = mediaType is null ? null : new(mediaType) } },
            RequestMessage = request,
        };

    [Fact]
    public async Task Html_page_gives_its_title_and_main_text_without_menus_and_scripts()
    {
        var html = $"""
            <html><head><title>Kariéra | Stavby Morava</title><meta property="og:title" content="Stavbyvedoucí"></head>
            <body><nav>Domů · O nás · Kontakt</nav><script>track()</script>
            <main><h1>Stavbyvedoucí</h1><p>{LongText}</p><ul><li>Mzda 60 000 Kč</li></ul></main>
            <footer>© 2026 Cookies</footer></body></html>
            """;

        var page = await Reader(r => StubHttpHandler.Html(r, html, "https://kariera.example.cz/final"))
            .ReadAsync(new Uri("https://kariera.example.cz/pozice"));

        Assert.Equal(new Uri("https://kariera.example.cz/final"), page.Url);     // after redirects
        Assert.Equal("Stavbyvedoucí", page.Title);
        Assert.Contains("- Mzda 60 000 Kč", page.Text);
        Assert.DoesNotContain("O nás", page.Text);
        Assert.DoesNotContain("track()", page.Text);
        Assert.DoesNotContain("Cookies", page.Text);
        Assert.Null(page.Document);
    }

    [Fact]
    public async Task Pdf_is_handed_over_as_a_document()
    {
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.7 inzerat");

        var page = await Reader(r => Bytes(r, pdf, "application/pdf")).ReadAsync(new Uri("https://example.cz/inzerat.pdf"));

        Assert.Equal(pdf, page.Document!.Content);
        Assert.Equal("application/pdf", page.Document.MediaType);
    }

    [Fact]
    public async Task Page_with_almost_no_text_asks_for_the_text_instead()
    {
        var ex = await Assert.ThrowsAsync<WebPageUnavailableException>(() =>
            Reader(r => StubHttpHandler.Html(r, "<html><body><div id=app></div></body></html>"))
                .ReadAsync(new Uri("https://spa.example.cz/job/1")));

        Assert.Contains("vložte ho jako text", ex.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "neexistuje")]
    [InlineData(HttpStatusCode.Forbidden, "403")]
    public async Task Error_answers_are_explained(HttpStatusCode status, string expected)
    {
        var ex = await Assert.ThrowsAsync<WebPageUnavailableException>(() =>
            Reader(r => new HttpResponseMessage(status) { RequestMessage = r }).ReadAsync(new Uri("https://example.cz/x")));

        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public async Task Other_content_types_are_refused()
    {
        await Assert.ThrowsAsync<WebPageUnavailableException>(() =>
            Reader(r => Bytes(r, [1, 2, 3], "image/png")).ReadAsync(new Uri("https://example.cz/logo.png")));
    }

    [Fact]
    public async Task Too_big_a_page_is_not_read_to_the_end()
    {
        var big = new byte[WebPageReader.MaxBytes + 1];

        await Assert.ThrowsAsync<WebPageUnavailableException>(() =>
            Reader(r => Bytes(r, big, "text/html")).ReadAsync(new Uri("https://example.cz/big")));
    }

    // ---------- SSRF ----------

    [Theory]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://localhost/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://10.0.0.5/")]
    [InlineData("http://[::1]/")]
    public async Task Addresses_inside_the_network_are_refused_before_connecting(string url)
    {
        using var http = new HttpClient(WebPageReader.SafeHandler());
        var reader = new WebPageReader(http, NullLogger<WebPageReader>.Instance);

        var ex = await Assert.ThrowsAsync<WebPageUnavailableException>(() => reader.ReadAsync(new Uri(url)));

        Assert.Contains("nelze načíst", ex.Message);
    }

    [Fact]
    public async Task Other_ports_are_refused()
    {
        var reader = Reader(_ => throw new InvalidOperationException("no request expected"));

        await Assert.ThrowsAsync<WebPageUnavailableException>(() => reader.ReadAsync(new Uri("https://example.cz:8443/admin")));
    }

    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("185.12.64.1", true)]
    [InlineData("2a00:1450:4014:80d::200e", true)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.20.0.1", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("::ffff:10.0.0.1", false)]
    [InlineData("2002:0a00:0001::1", false)]
    [InlineData("64:ff9b::a9fe:a9fe", false)]          // NAT64 wrapping 169.254.169.254
    [InlineData("64:ff9b::808:808", true)]             // NAT64 wrapping 8.8.8.8
    public void Only_public_addresses_are_public(string address, bool expected) =>
        Assert.Equal(expected, WebPageReader.IsPublic(IPAddress.Parse(address)));
}
