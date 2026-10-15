using System.Text;
using JobScout.Infrastructure.Web;

namespace JobScout.Tests.Infrastructure.Web;

/// <summary>A saved e-mail (.eml) taken apart – fictional messages only.</summary>
public sealed class MimeEmailReaderTests
{
    private static byte[] Eml(string text) => Encoding.UTF8.GetBytes(text);

    [Fact]
    public void Html_only_email_becomes_plain_text()
    {
        var email = new MimeEmailReader().Read(Eml("""
            From: HR <hr@firma.example>
            Subject: Pozice skladník
            Date: Mon, 13 Oct 2026 08:00:00 +0200
            Content-Type: text/html; charset=utf-8

            <html><body><p>Dobrý den,</p><p>hledáme <b>skladníka</b> do Olomouce.</p><ul><li>Směny</li></ul></body></html>
            """));

        Assert.Equal("Pozice skladník", email.Subject);
        Assert.Contains("hr@firma.example", email.From);
        Assert.Equal(new DateTimeOffset(2026, 10, 13, 8, 0, 0, TimeSpan.FromHours(2)), email.Date);
        Assert.Contains("hledáme skladníka do Olomouce.", email.Text);
        Assert.Contains("- Směny", email.Text);
        Assert.DoesNotContain("<b>", email.Text);
    }

    [Fact]
    public void Email_forwarded_as_an_attachment_is_part_of_the_text()
    {
        var email = new MimeEmailReader().Read(Eml("""
            From: Kamarád <kamarad@example.com>
            Subject: Fw: nabídka
            Content-Type: multipart/mixed; boundary="B"

            --B
            Content-Type: text/plain; charset=utf-8

            Koukni na tohle.
            --B
            Content-Type: message/rfc822

            From: Agentura <jobs@agentura.example>
            Subject: Hledáme účetní
            Content-Type: text/plain; charset=utf-8

            Účetní na plný úvazek, Plzeň.
            --B--
            """));

        Assert.Contains("Koukni na tohle.", email.Text);
        Assert.Contains("--- Přeposlaný e-mail ---", email.Text);
        Assert.Contains("Předmět: Hledáme účetní", email.Text);
        Assert.Contains("Účetní na plný úvazek, Plzeň.", email.Text);
        Assert.Empty(email.Attachments);
    }

    [Fact]
    public void Something_that_is_not_an_email_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new MimeEmailReader().Read(Eml("jen nějaký text bez hlaviček")));
    }
}
