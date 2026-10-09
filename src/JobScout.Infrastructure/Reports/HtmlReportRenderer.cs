using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using JobScout.Application.Reports;

namespace JobScout.Infrastructure.Reports;

/// <summary>
/// Morning report as one self-contained HTML file (no external CSS/JS – opens offline, from disk).
/// Everything that comes from job ads or the AI is HTML-encoded: ads are untrusted input.
/// </summary>
public static class HtmlReportRenderer
{
    private static readonly CultureInfo Czech = new("cs-CZ");

    // Encodes <, >, &, quotes – but keeps Czech letters readable (WebUtility.HtmlEncode would turn "ý" into &#253;)
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Create(UnicodeRanges.All);

    public static string Render(MorningReport report)
    {
        var letterNo = 0;
        var sb = new StringBuilder();

        sb.Append($$"""
            <!DOCTYPE html>
            <html lang="cs">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Job Scout – {{E(report.GeneratedAt.ToLocalTime().ToString("d. M. yyyy", Czech))}}</title>
            <style>
              :root { --fg:#1f2328; --muted:#59636e; --line:#d1d9e0; --bg:#f6f8fa; --accent:#0969da; --good:#1a7f37; --mid:#9a6700; --low:#cf222e; }
              * { box-sizing: border-box; }
              body { font: 15px/1.5 system-ui, -apple-system, "Segoe UI", sans-serif; color: var(--fg); margin: 0; background: var(--bg); }
              main { max-width: 900px; margin: 0 auto; padding: 24px 16px 64px; }
              h1 { font-size: 22px; margin: 0 0 4px; }
              h2 { font-size: 18px; margin: 32px 0 12px; padding-bottom: 6px; border-bottom: 1px solid var(--line); }
              .stats { color: var(--muted); }
              .job { background: #fff; border: 1px solid var(--line); border-radius: 8px; padding: 14px 16px; margin: 12px 0; }
              .job h3 { font-size: 16px; margin: 0; }
              .job h3 a { color: var(--accent); text-decoration: none; }
              .meta { color: var(--muted); font-size: 13px; margin: 2px 0 8px; }
              .score { display: inline-block; min-width: 44px; text-align: center; font-weight: 600; border-radius: 12px; padding: 1px 8px; margin-right: 8px; color: #fff; }
              .s-good { background: var(--good); } .s-mid { background: var(--mid); } .s-low { background: var(--low); }
              ul { margin: 4px 0 8px 18px; padding: 0; }
              .label { font-weight: 600; font-size: 13px; color: var(--muted); }
              .letter { white-space: pre-wrap; background: var(--bg); border: 1px solid var(--line); border-radius: 6px; padding: 12px; margin-top: 8px; }
              button { font: inherit; font-size: 13px; padding: 4px 12px; border: 1px solid var(--line); border-radius: 6px; background: #fff; cursor: pointer; margin-top: 8px; }
              button:hover { border-color: var(--accent); }
              details summary { cursor: pointer; color: var(--accent); }
              .warn { color: var(--mid); font-size: 13px; }
              .empty { color: var(--muted); font-style: italic; }
            </style>
            </head>
            <body>
            <main>
            <h1>Job Scout – ranní report</h1>
            <p class="stats">{{E(report.Subject)}} · {{E(report.GeneratedAt.ToLocalTime().ToString("d. M. yyyy HH:mm", Czech))}} ·
              nalezeno {{report.Stats.Found}}, nových {{report.Stats.NewForUser}}, ohodnoceno {{report.Stats.Evaluated}},
              dopisů {{report.Stats.Letters}} · náklady běhu ${{report.Stats.CostUsd.ToString("0.00", Czech)}}</p>
            """);

        sb.Append("<h2>Dnes doporučené (7+)</h2>");
        if (report.NewRecommended.Count == 0 && report.NewOther.Count == 0)
            sb.Append("<p class=\"empty\">Dnes nic nového.</p>");
        foreach (var job in report.NewRecommended)
            AppendJob(sb, job, ref letterNo);

        if (report.NewOther.Count > 0)
        {
            sb.Append($"<h2>Dnes ohodnocené – další ({report.NewOther.Count})</h2>");
            foreach (var job in report.NewOther)
                AppendJob(sb, job, ref letterNo);
        }

        if (report.EarlierRecommended.Count > 0)
        {
            sb.Append($"<h2>Dříve doporučené, zatím bez reakce ({report.EarlierRecommended.Count})</h2><details><summary>Zobrazit</summary>");
            foreach (var job in report.EarlierRecommended)
                AppendJob(sb, job, ref letterNo);
            sb.Append("</details>");
        }

        if (report.WaitingForUser.Count > 0)
        {
            sb.Append($"<h2>Čeká na tvé rozhodnutí ({report.WaitingForUser.Count})</h2>");
            sb.Append("<p class=\"stats\">Podle titulku spíš ne, ale proč ne – automaticky se nehodnotí. Rozklikni, co tě zaujme.</p><details><summary>Zobrazit</summary><ul>");
            foreach (var job in report.WaitingForUser)
                sb.Append($"<li>{Title(job)} <span class=\"meta\">· {E(job.Company)} · {E(job.Location)}</span></li>");
            sb.Append("</ul></details>");
        }

        sb.Append("""
            <script>
              function copyLetter(id, button) {
                navigator.clipboard.writeText(document.getElementById(id).innerText).then(() => {
                  const text = button.textContent; button.textContent = 'Zkopírováno ✓';
                  setTimeout(() => button.textContent = text, 1500);
                });
              }
            </script>
            </main>
            </body>
            </html>
            """);
        return sb.ToString();
    }

    private static void AppendJob(StringBuilder sb, ReportJob job, ref int letterNo)
    {
        var scoreClass = job.Score >= 7 ? "s-good" : job.Score >= 5 ? "s-mid" : "s-low";
        sb.Append("<article class=\"job\">");
        sb.Append($"<h3>{(job.Score is { } s ? $"<span class=\"score {scoreClass}\">{s}/10</span>" : "")}{Title(job)}</h3>");
        sb.Append($"<div class=\"meta\">{E(job.Company)} · {E(job.Location)}{(string.IsNullOrEmpty(job.Salary) ? "" : " · " + E(job.Salary))}</div>");
        if (job.DetailIncomplete)
            sb.Append("<div class=\"warn\">Inzerát se nepodařilo načíst celý – hodnoceno z útržku, ověř na odkazu.</div>");
        if (!string.IsNullOrEmpty(job.Summary))
            sb.Append($"<p>{E(job.Summary)}</p>");
        AppendList(sb, "Proč", job.Reasons);
        AppendList(sb, "Rizika", job.Risks);
        AppendList(sb, "Ověřit", job.Questions);

        if (!string.IsNullOrEmpty(job.CoverLetter))
        {
            letterNo++;
            var id = $"letter-{letterNo}";
            sb.Append($"<details><summary>Návrh průvodního dopisu</summary><div class=\"letter\" id=\"{id}\">{E(job.CoverLetter)}</div>");
            sb.Append($"<button type=\"button\" onclick=\"copyLetter('{id}', this)\">Kopírovat dopis</button></details>");
        }
        sb.Append("</article>");
    }

    private static void AppendList(StringBuilder sb, string label, IReadOnlyList<string> items)
    {
        if (items.Count == 0)
            return;
        sb.Append($"<div class=\"label\">{label}</div><ul>");
        foreach (var item in items)
            sb.Append($"<li>{E(item)}</li>");
        sb.Append("</ul>");
    }

    /// <summary>Title as a link – only for http(s) URLs (a "javascript:" link from an ad must never be clickable).</summary>
    private static string Title(ReportJob job) =>
        Uri.TryCreate(job.Url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? $"<a href=\"{E(uri.AbsoluteUri)}\" target=\"_blank\" rel=\"noopener\">{E(job.Title)}</a>"
            : E(job.Title);

    private static string E(string? text) => Encoder.Encode(text ?? "");
}
