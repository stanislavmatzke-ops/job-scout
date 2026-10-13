using System.IO.Compression;
using System.Text;

namespace JobScout.Tests.TestUtils;

/// <summary>A minimal Word document (.docx = ZIP with word/document.xml) – fictional content, built in memory.</summary>
internal static class DocxBuilder
{
    public const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <param name="bodyXml">what goes inside &lt;w:body&gt;, with the "w:" prefix</param>
    public static byte[] Build(string bodyXml, bool withDocumentXml = true)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml", """<?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"/>""");
            if (withDocumentXml)
                Add(zip, "word/document.xml",
                    $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:document xmlns:w="{W}"><w:body>{bodyXml}</w:body></w:document>""");
        }
        return buffer.ToArray();
    }

    /// <summary>One paragraph with one run of text.</summary>
    public static string P(string text) => $"""<w:p><w:r><w:t xml:space="preserve">{System.Security.SecurityElement.Escape(text)}</w:t></w:r></w:p>""";

    private static void Add(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
