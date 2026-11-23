using System.Net;
using JobScout.Infrastructure.JobsCz;
using JobScout.Tests.TestUtils;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobScout.Tests.Infrastructure.JobsCz;

/// <summary>
/// jobs.cz sends ≈ 1 in 5 visitors to its beta site, which our parser doesn't read (13 and 23 Nov 2026: whole morning
/// runs failed). A request that lands on the beta is asked again; anything else passes untouched.
/// </summary>
public class ClassicSiteHandlerTests
{
    private const string Search = "https://www.jobs.cz/prace/programator/?date=24h";

    /// <summary>Answers in order: true = "redirected to the beta" (the final address is on beta.jobs.cz).</summary>
    private static (HttpClient Client, StubHttpHandler Stub) Create(params bool[] beta)
    {
        var i = 0;
        var stub = new StubHttpHandler(request =>
        {
            var toBeta = beta[Math.Min(i++, beta.Length - 1)];
            var final = toBeta ? new Uri("https://beta.jobs.cz/prace/programator/") : request.RequestUri!;
            return new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = new HttpRequestMessage(HttpMethod.Get, final) };
        });
        var handler = new ClassicSiteHandler(NullLogger<ClassicSiteHandler>.Instance) { InnerHandler = stub };
        return (new HttpClient(handler), stub);
    }

    [Fact]
    public async Task The_classic_site_passes_with_one_request()
    {
        var (client, stub) = Create(false);

        var response = await client.GetAsync(Search);

        Assert.Equal("www.jobs.cz", response.RequestMessage!.RequestUri!.Host);
        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task A_request_sent_to_the_beta_is_asked_again_until_the_classic_site_answers()
    {
        var (client, stub) = Create(true, true, false);

        var response = await client.GetAsync(Search);

        Assert.Equal("www.jobs.cz", response.RequestMessage!.RequestUri!.Host);
        Assert.Equal(3, stub.Requests.Count);
        Assert.All(stub.Requests, u => Assert.Equal(Search, u.ToString()));
    }

    [Fact]
    public async Task After_the_last_attempt_the_beta_answer_is_returned_and_the_parser_reports_it()
    {
        var (client, stub) = Create(true);

        var response = await client.GetAsync(Search);

        Assert.Equal(ClassicSiteHandler.BetaHost, response.RequestMessage!.RequestUri!.Host);
        Assert.Equal(ClassicSiteHandler.MaxAttempts, stub.Requests.Count);
    }

    [Fact]
    public async Task Other_hosts_are_not_retried()
    {
        // A company's own career page (detail redirect) is a normal answer, not the beta
        var stub = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://kariera.example.cz/pozice/1"),
        });
        var client = new HttpClient(new ClassicSiteHandler(NullLogger<ClassicSiteHandler>.Instance) { InnerHandler = stub });

        await client.GetAsync("https://www.jobs.cz/rpd/2001234567/");

        Assert.Single(stub.Requests);
    }
}
