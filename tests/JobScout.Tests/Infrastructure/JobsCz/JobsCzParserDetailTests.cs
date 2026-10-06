using JobScout.Infrastructure.JobsCz;
using JobScout.Tests.TestUtils;

namespace JobScout.Tests.Infrastructure.JobsCz;

/// <summary>Job detail on www.jobs.cz (/rpd/{id}/) and the snippet fallback for redirected pages.</summary>
public class JobsCzParserDetailTests
{
    private static readonly Uri JobsCzUrl = new("https://www.jobs.cz/rpd/1000000001/");
    private static readonly Uri CareerPageUrl = new("https://ukazka.jobs.cz/detail-pozice?r=detail&id=1000000002");

    [Fact]
    public void Full_detail_is_complete_with_title_and_company()
    {
        var detail = JobsCzParser.ParseDetail(Fixture.JobsCz("detail-full.html"), "1000000001", JobsCzUrl);

        Assert.True(detail.IsComplete);
        Assert.Equal("1000000001", detail.ExternalId);
        Assert.Equal("Senior .NET vývojář (m/ž)", detail.Title);
        Assert.Equal("Ukázková firma s.r.o.", detail.Company);
        Assert.Equal("https://www.jobs.cz/rpd/1000000001/", detail.Url);
    }

    [Fact]
    public void Info_items_are_written_as_label_colon_value()
    {
        var text = JobsCzParser.ParseDetail(Fixture.JobsCz("detail-full.html"), "1000000001", JobsCzUrl).Text;

        Assert.Contains("Lokalita: Ukázková 1, Praha – Karlín", text);
        Assert.Contains("Společnost: Ukázková firma s.r.o.", text);
        Assert.Contains("Požadované jazyky: Čeština (Výborná), Angličtina (Pokročilá)", text);
        Assert.Contains("Typ pracovního poměru: Práce na plný úvazek", text);
        Assert.Contains("Benefity: Stravenky/příspěvek na stravování, Notebook", text);
    }

    [Fact]
    public void Body_keeps_paragraphs_and_bullet_points()
    {
        var text = JobsCzParser.ParseDetail(Fixture.JobsCz("detail-full.html"), "1000000001", JobsCzUrl).Text;

        // Paragraphs are separate lines – not glued together like TextContent would do
        Assert.Contains("Koho hledáme?\nHledáme zkušeného vývojáře do malého týmu.", text);
        Assert.DoesNotContain("Koho hledáme?Hledáme", text);

        // <li> → "- " bullet, &nbsp; → normal space
        Assert.Contains("\n- Návrh architektury nových služeb", text);
        Assert.Contains("\n- Vývoj v .NET a Azure", text);
    }

    [Fact]
    public void Company_intro_comes_before_the_job_text()
    {
        var text = JobsCzParser.ParseDetail(Fixture.JobsCz("detail-full.html"), "1000000001", JobsCzUrl).Text;

        Assert.True(text.IndexOf("Kdo jsme?", StringComparison.Ordinal) < text.IndexOf("Koho hledáme?", StringComparison.Ordinal));
    }

    [Fact]
    public void Missing_sections_do_not_leave_empty_gaps()
    {
        // No company intro and no benefits in this page
        var text = JobsCzParser.ParseDetail(Fixture.JobsCz("detail-no-header.html"), "1000000003", JobsCzUrl).Text;

        Assert.DoesNotContain("\n\n\n", text);
        Assert.DoesNotContain("Benefity:", text);
        Assert.Contains("Pro našeho klienta hledáme programátora C#.", text);
    }

    [Fact]
    public void Redirected_shell_without_body_returns_incomplete_snippet()
    {
        // What HttpClient gets from a JS-rendered company career page
        var detail = JobsCzParser.ParseDetail(Fixture.JobsCz("career-shell.html"), "1000000002", CareerPageUrl);

        Assert.False(detail.IsComplete);
        Assert.Equal("Tech Lead .NET - Detail pozice | Ukázka", detail.Title);
        Assert.Contains("ukazka.jobs.cz", detail.Text);                         // says where the full text is
        Assert.Contains("Hledáme tech leada pro vymyšlené projekty.", detail.Text); // og:description snippet
        Assert.Equal("https://www.jobs.cz/rpd/1000000002/", detail.Url);          // canonical jobs.cz URL
    }
}
