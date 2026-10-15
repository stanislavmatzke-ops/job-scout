using System.Net;
using System.Net.Http.Json;
using System.Text;
using JobScout.Application.Llm;
using JobScout.Application.ManualJobs;
using JobScout.Tests.TestUtils;

namespace JobScout.Tests.Api;

/// <summary>POST /api/matches/manual – "Přidat nabídku" (ADR-0013): multipart form with text, link or file.</summary>
public sealed class ManualJobEndpointsTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 15, 6, 0, 0, TimeSpan.Zero);
    private const string AdText = "Hledáme stavbyvedoucího pro rekonstrukce v Brně.";

    private readonly JobScoutApiFactory _factory = new();

    public ManualJobEndpointsTests()
    {
        _factory.AiAnswers[LlmPurpose.JobImport] =
            (LlmJson.Serialize(new ExtractedJobAd(true, "Stavbyvedoucí", "Stavby Morava a.s.", "Brno")), "end_turn");
        _factory.AiAnswers[LlmPurpose.Evaluation] = (MatchSeeder.EvaluationJson(8, "Sedí obor."), "end_turn");
    }

    public void Dispose() => _factory.Dispose();

    private sealed record Me(Guid Id);
    private sealed record Added(Guid Id, bool Evaluated, bool AlreadyInList);
    private sealed record History(string From, string To);
    private sealed record Detail(Guid Id, string Title, string Company, string Source, string Status, int? Score,
        bool AddedManually, List<History> History);

    private async Task<HttpClient> SignInAsync(string persona = "private", bool withProfile = true)
    {
        var browser = _factory.CreateBrowser();
        await browser.PostAsync($"/api/auth/dev/{persona}", null);
        var me = (await browser.GetFromJsonAsync<Me>("/api/me"))!.Id;
        if (withProfile)
        {
            await using var db = _factory.CreateDbContext();
            ProfileSeeder.Add(db, me, T0);
        }
        return browser;
    }

    private static MultipartFormDataContent Form(params (string Name, string Value)[] fields)
    {
        var form = new MultipartFormDataContent();
        foreach (var (name, value) in fields)
            form.Add(new StringContent(value), name);
        return form;
    }

    [Fact]
    public async Task Needs_sign_in()
    {
        var response = await _factory.CreateBrowser().PostAsync("/api/matches/manual", Form(("text", AdText)));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Pasted_text_is_added_evaluated_and_shown_in_the_detail()
    {
        var browser = await SignInAsync();

        var response = await browser.PostAsync("/api/matches/manual", Form(("text", AdText), ("status", "Applied")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var added = (await response.Content.ReadFromJsonAsync<Added>())!;
        Assert.True(added.Evaluated);
        var detail = (await browser.GetFromJsonAsync<Detail>($"/api/matches/{added.Id}"))!;
        Assert.Equal(("Stavbyvedoucí", "Stavby Morava a.s.", "manual", "Applied", 8, true),
            (detail.Title, detail.Company, detail.Source, detail.Status, detail.Score, detail.AddedManually));
        Assert.Equal(new History("New", "Applied"), Assert.Single(detail.History));
    }

    [Fact]
    public async Task A_file_can_be_uploaded_without_evaluation()
    {
        var browser = await SignInAsync();
        var form = Form(("evaluate", "false"));
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(AdText)), "file", "inzerat.txt");

        var response = await browser.PostAsync("/api/matches/manual", form);

        var added = (await response.Content.ReadFromJsonAsync<Added>())!;
        Assert.False(added.Evaluated);
        Assert.Null((await browser.GetFromJsonAsync<Detail>($"/api/matches/{added.Id}"))!.Score);
    }

    [Fact]
    public async Task Not_a_job_ad_is_422_with_the_reason()
    {
        _factory.AiAnswers[LlmPurpose.JobImport] = (LlmJson.Serialize(new ExtractedJobAd(false, "", "", "")), "end_turn");
        var browser = await SignInAsync();

        var response = await browser.PostAsync("/api/matches/manual", Form(("text", "Newsletter")));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("NotAJobAd", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("status", "Bogus")]
    [InlineData("status", "3")]
    [InlineData("url", "nejde-o-adresu")]
    public async Task Invalid_input_is_400(string field, string value)
    {
        var browser = await SignInAsync();

        var response = await browser.PostAsync("/api/matches/manual", Form(("text", AdText), (field, value)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Without_a_profile_evaluation_is_409()
    {
        var browser = await SignInAsync(withProfile: false);

        var response = await browser.PostAsync("/api/matches/manual", Form(("text", AdText)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
