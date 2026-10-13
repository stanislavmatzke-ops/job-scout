using System.IO.Compression;
using System.Text;
using System.Xml;

namespace JobScout.Application.Profiles;

/// <summary>
/// The text of a Word document (.docx), without any library: a .docx is a ZIP with the body in word/document.xml.
/// Paragraphs become lines, table cells are separated by " | ", tabs and line breaks are kept. Formatting, pictures,
/// headers and footers are left out – the AI needs the words, not the layout. The old binary .doc is not a ZIP and is refused.
/// </summary>
public static class DocxText
{
    public const string MediaType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    /// <summary>Never read more than this from the archive – a 5 MB upload must not unpack into gigabytes.</summary>
    private const long MaxXmlBytes = 20 * 1024 * 1024;

    /// <summary>More than enough for any CV (≈ 40 pages); the rest is cut off.</summary>
    public const int MaxChars = 100_000;

    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <exception cref="ArgumentException">not a Word document, or no text in it (e.g. a scanned CV pasted as a picture)</exception>
    public static string Extract(byte[] docx)
    {
        if (!docx.AsSpan().StartsWith("PK"u8))
            throw new ArgumentException(NotADocx, nameof(docx));

        string text;
        try
        {
            using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
            var body = zip.GetEntry("word/document.xml") ?? throw new ArgumentException(NotADocx, nameof(docx));
            if (body.Length > MaxXmlBytes)
                throw new ArgumentException("Dokument Word je příliš velký.", nameof(docx));

            using var stream = Bounded(body.Open());
            text = Read(stream);
        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException)
        {
            throw new ArgumentException(NotADocx, nameof(docx), ex);
        }

        text = Tidy(text);
        if (text.Length == 0)
            throw new ArgumentException("V dokumentu Word jsme nenašli žádný text. Pokud je životopis vložený jako obrázek, nahrajte ho prosím jako PDF.", nameof(docx));
        return text.Length > MaxChars ? text[..MaxChars] : text;
    }

    /// <summary>
    /// The declared size can lie (a "zip bomb") – the XML is unpacked into memory, but never more than the limit.
    /// </summary>
    private static MemoryStream Bounded(Stream entry)
    {
        using (entry)
        {
            var copy = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = entry.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (copy.Length + read > MaxXmlBytes)
                    throw new ArgumentException("Dokument Word je příliš velký.", "docx");
                copy.Write(buffer, 0, read);
            }
            copy.Position = 0;
            return copy;
        }
    }

    private const string NotADocx = "Soubor má příponu .docx, ale nejde o platný dokument Word. Uložte ho prosím znovu jako .docx nebo PDF.";

    private static string Read(Stream xml)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true };
        using var reader = XmlReader.Create(xml, settings);
        var text = new StringBuilder();
        var inRun = false;                  // tabs and breaks count only inside a run (w:tab also defines tab stops)
        var inText = false;
        var cellHasText = false;

        while (reader.Read())
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Element when reader.NamespaceURI == W:
                    switch (reader.LocalName)
                    {
                        case "r":
                            inRun = !reader.IsEmptyElement;
                            break;
                        case "t":                               // a piece of text; its content comes as the next nodes
                            inText = !reader.IsEmptyElement;
                            break;
                        case "tab" when inRun:
                            text.Append('\t');
                            break;
                        case "br" or "cr" when inRun:
                            text.Append('\n');
                            break;
                        case "p" when reader.IsEmptyElement:   // <w:p/> – an empty line
                            text.Append('\n');
                            break;
                        case "tc":                              // a new table cell
                            cellHasText = false;
                            break;
                    }
                    break;

                case XmlNodeType.Text or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace when inText:
                    text.Append(reader.Value);
                    cellHasText = true;
                    break;

                case XmlNodeType.EndElement when reader.NamespaceURI == W:
                    switch (reader.LocalName)
                    {
                        case "t":
                            inText = false;
                            break;
                        case "r":
                            inRun = false;
                            break;
                        case "p":                               // end of a paragraph = end of a line
                            text.Append('\n');
                            break;
                        case "tc" when cellHasText:             // the cells of one row on one line
                            TrimEndNewLines(text);
                            text.Append(" | ");
                            break;
                        case "tr":
                            TrimEnd(text, " | ");
                            text.Append('\n');
                            break;
                    }
                    break;
            }
        }
        return text.ToString();
    }

    private static void TrimEndNewLines(StringBuilder text)
    {
        while (text.Length > 0 && text[^1] == '\n')
            text.Length--;
    }

    private static void TrimEnd(StringBuilder text, string suffix)
    {
        TrimEndNewLines(text);
        if (text.Length >= suffix.Length && text.ToString(text.Length - suffix.Length, suffix.Length) == suffix)
            text.Length -= suffix.Length;
    }

    /// <summary>Trailing spaces off every line, at most one empty line in a row.</summary>
    private static string Tidy(string text)
    {
        var lines = text.Replace("\r", "").Split('\n').Select(l => l.TrimEnd());
        var result = new StringBuilder();
        var empty = 0;
        foreach (var line in lines)
        {
            empty = line.Length == 0 ? empty + 1 : 0;
            if (empty <= 1)
                result.Append(line).Append('\n');
        }
        return result.ToString().Trim();
    }
}
