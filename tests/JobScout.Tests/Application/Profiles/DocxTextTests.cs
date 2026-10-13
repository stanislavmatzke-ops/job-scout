using System.Text;
using JobScout.Application.Llm;
using JobScout.Application.Profiles;
using JobScout.Core.Domain;
using JobScout.Tests.TestUtils;

namespace JobScout.Tests.Application.Profiles;

/// <summary>CV in Word (13 Oct): the .docx is turned into plain text here – Claude reads PDF and text, not Word.</summary>
public sealed class DocxTextTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 13, 8, 0, 0, TimeSpan.Zero);

    private readonly SqliteTestDatabase _database = new();
    private readonly FixedTimeProvider _time = new(T0);
    private readonly FakeLlmClient _llm = new(LlmJson.Serialize(TestProfiles.With(["Softwarový architekt"])));
    private readonly Guid _userId;

    public DocxTextTests()
    {
        using var db = _database.CreateContext();
        var user = new User("google", "g-123", "jana@example.com", "Jana", T0);
        db.Users.Add(user);
        db.SaveChanges();
        _userId = user.Id;
    }

    public void Dispose() => _database.Dispose();

    private async Task<AnalyzedProfile> UploadAsync(string fileName, byte[] content)
    {
        await using var db = _database.CreateContext();
        return await new UploadCv(db, new AnalyzeProfile(db, new ProfileExtractor(_llm), _time)).ExecuteAsync(_userId, fileName, content);
    }

    // ---------- reading the text ----------

    [Fact]
    public void Paragraphs_become_lines()
    {
        var docx = DocxBuilder.Build(DocxBuilder.P("Jana Ukázková") + DocxBuilder.P("Senior .NET vývojářka & architektka"));

        Assert.Equal("Jana Ukázková\nSenior .NET vývojářka & architektka", DocxText.Extract(docx));
    }

    [Fact]
    public void Runs_tabs_and_line_breaks_are_kept_tab_stop_definitions_are_not_text()
    {
        var docx = DocxBuilder.Build("""
            <w:p>
              <w:pPr><w:tabs><w:tab w:val="left" w:pos="2000"/></w:tabs></w:pPr>
              <w:r><w:t>2019 – 2026</w:t></w:r><w:r><w:tab/><w:t xml:space="preserve">Architekt, </w:t></w:r><w:r><w:t>Firma a.s.</w:t></w:r>
              <w:r><w:br/><w:t>Brno</w:t></w:r>
            </w:p>
            """);

        Assert.Equal("2019 – 2026\tArchitekt, Firma a.s.\nBrno", DocxText.Extract(docx));
    }

    [Fact]
    public void Table_cells_of_a_row_share_a_line()
    {
        var docx = DocxBuilder.Build("""
            <w:tbl>
              <w:tr><w:tc><w:p><w:r><w:t>Jazyky</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>C#, SQL</w:t></w:r></w:p></w:tc></w:tr>
              <w:tr><w:tc><w:p><w:r><w:t>Cloud</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>Azure</w:t></w:r></w:p></w:tc></w:tr>
            </w:tbl>
            """ + DocxBuilder.P("Konec"));

        Assert.Equal("Jazyky | C#, SQL\nCloud | Azure\nKonec", DocxText.Extract(docx));
    }

    [Fact]
    public void Empty_paragraphs_leave_at_most_one_empty_line()
    {
        var docx = DocxBuilder.Build(DocxBuilder.P("Profil") + "<w:p/><w:p/><w:p></w:p>" + DocxBuilder.P("Praxe"));

        Assert.Equal("Profil\n\nPraxe", DocxText.Extract(docx));
    }

    [Theory]
    [InlineData("not a zip at all")]
    [InlineData("PK but broken")]
    public void Something_that_is_not_a_docx_is_refused(string content)
    {
        Assert.Throws<ArgumentException>(() => DocxText.Extract(Encoding.UTF8.GetBytes(content)));
    }

    [Fact]
    public void A_zip_without_a_word_body_is_refused()
    {
        Assert.Throws<ArgumentException>(() => DocxText.Extract(DocxBuilder.Build("", withDocumentXml: false)));
    }

    [Fact]
    public void A_document_without_text_asks_for_a_pdf()
    {
        var ex = Assert.Throws<ArgumentException>(() => DocxText.Extract(DocxBuilder.Build("<w:p/><w:p><w:r><w:drawing/></w:r></w:p>")));

        Assert.Contains("PDF", ex.Message);
    }

    // ---------- uploading ----------

    [Fact]
    public async Task Word_cv_goes_to_the_ai_as_text_and_is_stored_as_text()
    {
        var docx = DocxBuilder.Build(DocxBuilder.P("Jana Ukázková") + DocxBuilder.P("Senior .NET vývojářka, Brno"));

        var analyzed = await UploadAsync("Životopis Jana.docx", docx);

        var sent = Assert.Single(Assert.Single(_llm.Requests).Documents);
        Assert.Equal(("text/plain", "Jana Ukázková\nSenior .NET vývojářka, Brno"), (sent.MediaType, Encoding.UTF8.GetString(sent.Content)));
        Assert.Equal("Životopis Jana.docx", analyzed.Version.CvFileName);

        await using var db = _database.CreateContext();
        var stored = db.StoredCvs.Single(c => c.UserId == _userId);
        Assert.Equal(("Životopis Jana.docx", "text/plain"), (stored.FileName, stored.MediaType));   // notes can re-analyse it later
    }

    [Fact]
    public async Task The_same_word_cv_again_costs_nothing()
    {
        var docx = DocxBuilder.Build(DocxBuilder.P("Jana Ukázková"));

        await UploadAsync("cv.docx", docx);
        await UploadAsync("cv.docx", docx);

        Assert.Equal(1, _llm.Calls);
    }

    [Fact]
    public async Task The_old_binary_doc_is_refused_with_advice()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => UploadAsync("cv.doc", [0xD0, 0xCF, 0x11, 0xE0]));

        Assert.Contains(".docx", ex.Message);
        Assert.Equal(0, _llm.Calls);
    }
}
