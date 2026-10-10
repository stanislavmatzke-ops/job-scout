using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using JobScout.Application.Llm;
using JobScout.Tests.TestUtils;

namespace JobScout.Tests.Api;

/// <summary>/api/profile – the UI's "Můj profil": upload a CV (multipart), notes, grammatical gender.</summary>
public sealed class ProfileEndpointsTests : IDisposable
{
    private static readonly byte[] CvMarkdown = "# Jana Ukázková\nSenior .NET vývojářka, Brno"u8.ToArray();

    private readonly JobScoutApiFactory _factory = new();

    public ProfileEndpointsTests()
    {
        _factory.AiAnswers[LlmPurpose.Profile] = (LlmJson.Serialize(TestProfiles.With(["Softwarový architekt"])), "end_turn");
    }

    public void Dispose() => _factory.Dispose();

    private sealed record CandidateProfile(string Headline);
    private sealed record ProfileResponse(int? Version, string? CvFileName, string? Notes, CandidateProfile? Profile, string Gender, bool HasCv);

    private async Task<HttpClient> SignInAsync()
    {
        var browser = _factory.CreateBrowser();
        await browser.PostAsync("/api/auth/dev/private", null);
        return browser;
    }

    private static MultipartFormDataContent CvForm(string fileName, byte[] content)
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    [Fact]
    public async Task Profile_needs_sign_in()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateBrowser().GetAsync("/api/profile")).StatusCode);
    }

    [Fact]
    public async Task New_user_has_an_empty_profile()
    {
        var browser = await SignInAsync();

        var profile = await browser.GetFromJsonAsync<ProfileResponse>("/api/profile");

        Assert.NotNull(profile);
        Assert.Null(profile.Profile);
        Assert.False(profile.HasCv);
        Assert.Equal("Unspecified", profile.Gender);
    }

    [Fact]
    public async Task Uploaded_cv_returns_the_analysed_profile()
    {
        var browser = await SignInAsync();

        var response = await browser.PutAsync("/api/profile/cv", CvForm("zivotopis.md", CvMarkdown));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = await response.Content.ReadFromJsonAsync<ProfileResponse>();
        Assert.Equal(((int?)1, "zivotopis.md", true), (profile!.Version, profile.CvFileName, profile.HasCv));
        Assert.Equal("Testovací profil", profile.Profile!.Headline);
    }

    [Fact]
    public async Task Unsupported_file_is_400_and_costs_nothing()
    {
        var browser = await SignInAsync();

        var response = await browser.PutAsync("/api/profile/cv", CvForm("cv.docx", CvMarkdown));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _factory.Llm.Calls);
    }

    [Fact]
    public async Task Upload_without_a_file_is_400()
    {
        var browser = await SignInAsync();

        var response = await browser.PutAsync("/api/profile/cv", new MultipartFormDataContent { { new StringContent("x"), "other" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Notes_before_any_cv_are_409()
    {
        var browser = await SignInAsync();

        var response = await browser.PutAsJsonAsync("/api/profile/notes", new { notes = "Chci full remote." });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Notes_re_analyse_the_stored_cv()
    {
        var browser = await SignInAsync();
        await browser.PutAsync("/api/profile/cv", CvForm("cv.md", CvMarkdown));

        var response = await browser.PutAsJsonAsync("/api/profile/notes", new { notes = "Chci full remote." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = await response.Content.ReadFromJsonAsync<ProfileResponse>();
        Assert.Equal(((int?)2, "Chci full remote."), (profile!.Version, profile.Notes));
        Assert.Equal(2, _factory.Llm.Calls);
    }

    [Fact]
    public async Task Gender_is_saved()
    {
        var browser = await SignInAsync();

        var response = await browser.PutAsJsonAsync("/api/profile/settings", new { gender = "Feminine" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("Feminine", (await browser.GetFromJsonAsync<ProfileResponse>("/api/profile"))!.Gender);
    }

    [Theory]
    [InlineData("""{ "gender": "Bogus" }""")]
    [InlineData("""{ "gender": 42 }""")]
    [InlineData("""{ }""")]
    public async Task Invalid_gender_is_400(string json)
    {
        var browser = await SignInAsync();

        var response = await browser.PutAsync("/api/profile/settings",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
