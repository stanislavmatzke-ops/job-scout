using System.Diagnostics;
using System.Net;
using JobScout.Infrastructure.JobsCz;
using JobScout.Tests.TestUtils;

namespace JobScout.Tests.Infrastructure.JobsCz;

/// <summary>The "be gentle to the site" handler: minimum gap between requests, one at a time.</summary>
public class PoliteDelayHandlerTests
{
    private static HttpClient CreateClient(TimeSpan gap) =>
        new(new PoliteDelayHandler(gap)
        {
            InnerHandler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)),
        });

    [Fact]
    public async Task First_request_is_not_delayed()
    {
        using var client = CreateClient(TimeSpan.FromSeconds(5));

        var sw = Stopwatch.StartNew();
        await client.GetAsync("https://example.test/1");

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"First request took {sw.Elapsed}");
    }

    [Fact]
    public async Task Consecutive_requests_keep_minimum_gap()
    {
        var gap = TimeSpan.FromMilliseconds(300);
        using var client = CreateClient(gap);

        var sw = Stopwatch.StartNew();
        await client.GetAsync("https://example.test/1");
        await client.GetAsync("https://example.test/2");
        await client.GetAsync("https://example.test/3");

        // 3 requests = at least 2 gaps (small tolerance for timer precision)
        Assert.True(sw.Elapsed >= gap * 2 - TimeSpan.FromMilliseconds(30), $"3 requests took only {sw.Elapsed}");
    }

    [Fact]
    public async Task Parallel_requests_are_serialized()
    {
        var gap = TimeSpan.FromMilliseconds(200);
        using var client = CreateClient(gap);

        // Even if someone fires them at once (e.g. an agent calling tools in parallel), they go one by one
        var sw = Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(1, 3).Select(i => client.GetAsync($"https://example.test/{i}")));

        Assert.True(sw.Elapsed >= gap * 2 - TimeSpan.FromMilliseconds(30), $"Parallel requests took only {sw.Elapsed}");
    }
}
