using System.Net;
using System.Text;

namespace JobScout.Tests.TestUtils;

/// <summary>
/// Fake HTTP "network": no real request leaves the test. Records what was requested
/// and answers with whatever the test decides.
/// </summary>
internal sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<Uri> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request.RequestUri!);
        return Task.FromResult(respond(request));
    }

    /// <summary>
    /// 200 OK with HTML. <paramref name="finalUrl"/> simulates a followed redirect:
    /// the real HttpClient reports the final address in response.RequestMessage.RequestUri.
    /// </summary>
    public static HttpResponseMessage Html(HttpRequestMessage request, string html, string? finalUrl = null) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(html, Encoding.UTF8, "text/html"),
            RequestMessage = finalUrl is null ? request : new HttpRequestMessage(HttpMethod.Get, finalUrl),
        };
}
