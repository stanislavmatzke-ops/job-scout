using AngleSharp.Html.Parser;
using JobScout.Infrastructure.JobsCz;

namespace JobScout.Tests.Infrastructure.JobsCz;

/// <summary>Text helpers: Clean (whitespace, invisible characters) and ToPlainText (HTML → readable text).</summary>
public class JobsCzParserTextTests
{
    [Theory]
    [InlineData("  Praha   –  Karlín  ", "Praha – Karlín")]          // extra spaces
    [InlineData("Řádek 1\n\t Řádek 2", "Řádek 1 Řádek 2")]           // newlines and tabs collapse
    [InlineData("65\u00A0000 \u200D–\u200D 80\u00A0000 Kč", "65 000 – 80 000 Kč")] // nbsp + zero-width joiner
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void Clean_normalizes_text(string? input, string expected)
    {
        Assert.Equal(expected, JobsCzParser.Clean(input));
    }

    [Fact]
    public void ToPlainText_turns_blocks_into_lines_and_list_items_into_bullets()
    {
        var root = new HtmlParser().ParseDocument("""
            <div id="root">
              <h2>Nadpis</h2>
              <p>První <strong>odstavec</strong>.</p>
              <ul><li>Bod A</li><li>Bod <em>B</em></li></ul>
              <p>Konec</p>
            </div>
            """).QuerySelector("#root");

        var text = JobsCzParser.ToPlainText(root);

        Assert.Equal("Nadpis\nPrvní odstavec.\n- Bod A\n- Bod B\nKonec", text);
    }

    [Fact]
    public void ToPlainText_of_missing_element_is_empty()
    {
        Assert.Equal("", JobsCzParser.ToPlainText(null));
    }
}
