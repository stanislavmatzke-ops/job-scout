using JobScout.Application.Llm;
using JobScout.Application.Profiles;
using JobScout.Core.Domain;
using JobScout.Tests.TestUtils;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Tests.Application.Profiles;

/// <summary>
/// The profile from the UI: upload a CV, change notes, choose grammatical gender, see the result.
/// Analysis still happens only when the input changes (AnalyzeProfile's hash) – the same rule as in the Worker.
/// </summary>
public sealed class ProfileUseCasesTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 10, 8, 0, 0, TimeSpan.Zero);
    private static readonly byte[] CvMarkdown = "# Jana Ukázková\nSenior .NET vývojářka, Brno"u8.ToArray();

    private readonly SqliteTestDatabase _database = new();
    private readonly FixedTimeProvider _time = new(T0);
    private readonly FakeLlmClient _llm = new(LlmJson.Serialize(TestProfiles.With(["Softwarový architekt"])));
    private readonly Guid _userId;

    public ProfileUseCasesTests()
    {
        using var db = _database.CreateContext();
        var user = new User("google", "g-123", "jana@example.com", "Jana", T0);
        db.Users.Add(user);
        db.SaveChanges();
        _userId = user.Id;
    }

    public void Dispose() => _database.Dispose();

    // ---------- helpers ----------

    private AnalyzeProfile Analyze(JobScout.Infrastructure.Persistence.JobScoutDbContext db) => new(db, new ProfileExtractor(_llm), _time);

    private async Task<AnalyzedProfile> UploadAsync(string fileName, byte[] content)
    {
        await using var db = _database.CreateContext();
        return await new UploadCv(db, Analyze(db)).ExecuteAsync(_userId, fileName, content);
    }

    private async Task<AnalyzedProfile?> UpdateNotesAsync(string? notes)
    {
        await using var db = _database.CreateContext();
        return await new UpdateProfileNotes(db, Analyze(db)).ExecuteAsync(_userId, notes);
    }

    private async Task<ProfileView> GetAsync()
    {
        await using var db = _database.CreateContext();
        return await new GetProfile(db).ExecuteAsync(_userId);
    }

    // ---------- view ----------

    [Fact]
    public async Task New_user_has_no_profile_yet()
    {
        var view = await GetAsync();

        Assert.Null(view.Profile);
        Assert.Null(view.Version);
        Assert.False(view.HasCv);
        Assert.Equal(GrammaticalGender.Unspecified, view.Gender);
    }

    // ---------- upload ----------

    [Fact]
    public async Task Uploaded_cv_is_analysed_and_stored()
    {
        var analyzed = await UploadAsync("zivotopis.md", CvMarkdown);

        Assert.True(analyzed.IsNew);
        var request = Assert.Single(_llm.Requests);
        Assert.Equal(CvMarkdown, Assert.Single(request.Documents).Content);
        Assert.Equal("text/plain", request.Documents[0].MediaType);

        var view = await GetAsync();
        Assert.Equal(((int?)1, "zivotopis.md", true), (view.Version, view.CvFileName, view.HasCv));
        Assert.Equal("Testovací profil", view.Profile!.Headline);
        Assert.Equal(T0, view.AnalyzedAt);
    }

    [Fact]
    public async Task Same_cv_again_costs_nothing()
    {
        await UploadAsync("cv.md", CvMarkdown);
        var again = await UploadAsync("cv.md", CvMarkdown);

        Assert.False(again.IsNew);
        Assert.Equal(1, _llm.Calls);
    }

    [Fact]
    public async Task Pdf_is_sent_as_a_pdf()
    {
        byte[] pdf = [.. "%PDF-1.7 fake but with the right header"u8];

        await UploadAsync("CV Jana.PDF", pdf);

        Assert.Equal("application/pdf", Assert.Single(Assert.Single(_llm.Requests).Documents).MediaType);
    }

    [Theory]
    [InlineData("cv.docx")]          // Word – not supported (yet)
    [InlineData("cv")]               // no extension
    [InlineData("cv.exe")]
    public async Task Unsupported_file_type_is_rejected(string fileName)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => UploadAsync(fileName, CvMarkdown));
        Assert.Equal(0, _llm.Calls);
    }

    [Fact]
    public async Task Pdf_that_is_not_a_pdf_is_rejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => UploadAsync("cv.pdf", CvMarkdown));
        Assert.Equal(0, _llm.Calls);
    }

    [Fact]
    public async Task Empty_or_too_big_file_is_rejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => UploadAsync("cv.md", []));
        await Assert.ThrowsAsync<ArgumentException>(() => UploadAsync("cv.md", new byte[StoredCv.MaxBytes + 1]));
        Assert.Equal(0, _llm.Calls);
    }

    // ---------- notes ----------

    [Fact]
    public async Task New_notes_re_analyse_the_stored_cv()
    {
        await UploadAsync("cv.md", CvMarkdown);

        var analyzed = await UpdateNotesAsync("Chci full remote, min. 95 000 Kč.");

        Assert.NotNull(analyzed);
        Assert.True(analyzed.IsNew);
        Assert.Equal(2, _llm.Calls);
        var second = _llm.Requests[1];
        Assert.Equal(CvMarkdown, Assert.Single(second.Documents).Content);   // no new upload needed
        Assert.Contains("Chci full remote", second.UserPrompt);

        var view = await GetAsync();
        Assert.Equal(((int?)2, "Chci full remote, min. 95 000 Kč."), (view.Version, view.Notes));
    }

    [Fact]
    public async Task New_cv_keeps_the_notes()
    {
        await UploadAsync("cv.md", CvMarkdown);
        await UpdateNotesAsync("Nechci Javu.");

        await UploadAsync("cv-2026.md", "# Jana Ukázková\nTech Lead"u8.ToArray());

        var view = await GetAsync();
        Assert.Equal(((int?)3, "cv-2026.md", "Nechci Javu."), (view.Version, view.CvFileName, view.Notes));
        Assert.Contains("Nechci Javu.", _llm.Requests[2].UserPrompt);
    }

    [Fact]
    public async Task Notes_without_a_cv_cannot_be_analysed()
    {
        Assert.Null(await UpdateNotesAsync("Chci full remote."));
        Assert.Equal(0, _llm.Calls);
    }

    [Fact]
    public async Task Too_long_notes_are_rejected()
    {
        await UploadAsync("cv.md", CvMarkdown);

        await Assert.ThrowsAsync<ArgumentException>(() => UpdateNotesAsync(new string('x', UpdateProfileNotes.MaxLength + 1)));
    }

    // ---------- settings ----------

    [Fact]
    public async Task Gender_is_saved()
    {
        await using (var db = _database.CreateContext())
            await new UpdateProfileSettings(db).ExecuteAsync(_userId, GrammaticalGender.Feminine);

        Assert.Equal(GrammaticalGender.Feminine, (await GetAsync()).Gender);
    }

    // ---------- the Worker path ----------

    [Fact]
    public async Task Worker_analysis_stores_the_cv_too()
    {
        // The Worker reads the CV from a folder – it must be stored as well, so the user can later change notes in the UI
        await using (var db = _database.CreateContext())
            await Analyze(db).ExecuteAsync(new ProfileInput("google", "g-123", "Jana", "cv.md",
                new LlmDocument("CV", "text/plain", CvMarkdown), Notes: null));

        await using var check = _database.CreateContext();
        var stored = await check.StoredCvs.SingleAsync(c => c.UserId == _userId);
        Assert.Equal(CvMarkdown, stored.Content);
    }
}
