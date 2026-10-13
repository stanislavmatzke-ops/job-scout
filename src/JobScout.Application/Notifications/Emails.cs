using System.Net;
using System.Text;
using JobScout.Application.Abstractions;
using JobScout.Application.Reports;
using JobScout.Core.Domain;

namespace JobScout.Application.Notifications;

/// <summary>
/// The e-mails of Job Scout (7f). We don't know which language the reader prefers (the UI language lives in their
/// browser), so every e-mail is in Czech and English – Czech first, then the same in English.
/// Formal "vy", the product speaks as "we" (copilot-instructions.md).
/// </summary>
public static class Emails
{
    public static EmailMessage AccessRequested(string to, User user, string appUrl) => Build(to,
        $"Job Scout: nová žádost o přístup – {user.DisplayName}",
        [
            $"{user.DisplayName} ({user.Email}, {user.Provider}) žádá o přístup.",
            user.AccessRequestMessage is { } m ? $"Zpráva: „{m}“" : "Bez zprávy.",
        ],
        [
            $"{user.DisplayName} ({user.Email}, {user.Provider}) asks for access.",
            user.AccessRequestMessage is { } m2 ? $"Message: “{m2}”" : "No message.",
        ],
        ("Otevřít správu / Open administration", $"{appUrl}/admin"));

    public static EmailMessage Approved(User user, string appUrl) => Build(user.Email,
        "Job Scout: přístup schválen / access approved",
        [
            $"Dobrý den, {user.DisplayName},",
            "váš přístup do Job Scout byl schválen. Přihlaste se stejným účtem jako při žádosti, nahrajte životopis a spusťte první hledání.",
        ],
        [
            $"Hello {user.DisplayName},",
            "your access to Job Scout has been approved. Sign in with the same account you used for the request, upload your CV and start your first search.",
        ],
        ("Přihlásit se / Sign in", appUrl));

    public static EmailMessage Rejected(User user, string appUrl) => Build(user.Email,
        "Job Scout: žádost nebyla schválena / request not approved",
        [
            $"Dobrý den, {user.DisplayName},",
            "vaši žádost o přístup do Job Scout jsme tentokrát neschválili.",
            user.RejectedReason ?? "",
            "Pokud se okolnosti změní, můžete po přihlášení požádat znovu.",
        ],
        [
            $"Hello {user.DisplayName},",
            "we haven’t approved your request for access to Job Scout this time.",
            user.RejectedReason ?? "",
            "If things change, you can sign in and ask again.",
        ],
        ("Job Scout", appUrl));

    public static EmailMessage Suspended(User user, string appUrl) => Build(user.Email,
        "Job Scout: účet pozastaven / account suspended",
        [
            $"Dobrý den, {user.DisplayName},",
            "váš účet v Job Scout je pozastaven. Své nabídky si můžete dál prohlížet, vyhledávání nových nabídek a funkce AI jsou však vypnuté.",
            user.SuspendedReason ?? "",
        ],
        [
            $"Hello {user.DisplayName},",
            "your Job Scout account is suspended. You can still browse your jobs, but the daily search and AI features are turned off.",
            user.SuspendedReason ?? "",
        ],
        ("Job Scout", appUrl));

    /// <summary>To the admin: what a user wrote in "Zpětná vazba". "Reply" goes to the user only when they asked for an answer.</summary>
    public static EmailMessage FeedbackReceived(string to, User user, Feedback feedback, string appUrl)
    {
        var (cs, en) = KindNames(feedback.Kind);
        var stars = feedback.Rating is { } r ? new string('★', r) + new string('☆', 5 - r) : null;
        var who = string.IsNullOrWhiteSpace(user.Email) ? $"{user.DisplayName} ({user.Provider})" : $"{user.DisplayName} ({user.Email}, {user.Provider})";
        var replyTo = feedback.ReplyWanted && !string.IsNullOrWhiteSpace(user.Email) ? user.Email : null;

        var message = Build(to,
            $"Job Scout: zpětná vazba ({cs}) – {user.DisplayName}",
            [
                $"{who} posílá zpětnou vazbu – {cs}.",
                stars is null ? "" : $"Hodnocení: {stars}",
                $"„{feedback.Message}“",
                replyTo is null ? "Odpověď si nepřeje (nebo nemá e-mail)." : "Přeje si odpověď – stačí na tento e-mail odpovědět.",
            ],
            [
                $"{who} sent feedback – {en}.",
                stars is null ? "" : $"Rating: {stars}",
                replyTo is null ? "No reply wanted (or no e-mail address)." : "Wants a reply – just answer this e-mail.",
            ],
            ("Otevřít správu / Open administration", $"{appUrl}/admin"));
        return message with { ReplyTo = replyTo };
    }

    /// <summary>To the admin: the app-wide brake on AI clicks engaged (ADR-0021).</summary>
    public static EmailMessage AiPaused(string to, decimal limitUsd, string appUrl) => Build(to,
        "Job Scout: funkce AI na kliknutí jsou pozastavené / AI on demand paused",
        [
            $"Kliknutí na funkce AI (profil, hodnocení, dopisy) stála za posledních 24 hodin všechny uživatele dohromady {limitUsd:0.00} $ nebo víc.",
            "Do té doby, než starší volání vypadnou z 24hodinového okna, jsou tyto funkce pro všechny kromě správců vypnuté. Ranní hledání běží dál.",
            "Ve správě se podívejte, kdo kolik utratil, případně limit AiLimits:AppCostPer24hUsd zvyšte.",
        ],
        [
            $"AI clicks (profile, evaluation, letters) of all users together cost {limitUsd:0.00} USD or more in the last 24 hours.",
            "Until older calls leave the 24-hour window, these features are off for everybody except the admins. The morning search keeps running.",
        ],
        ("Otevřít správu / Open administration", $"{appUrl}/admin"));

    private static (string Cs, string En) KindNames(FeedbackKind kind) => kind switch
    {
        FeedbackKind.Idea => ("nápad", "idea"),
        FeedbackKind.Problem => ("něco nefunguje", "something is broken"),
        FeedbackKind.Praise => ("pochvala", "praise"),
        _ => ("jiné", "other"),
    };

    /// <summary>The morning e-mail: today's new jobs, the recommended ones first. Null = nothing new, nothing to send.</summary>
    public static EmailMessage? Morning(User user, MorningReport report, string appUrl)
    {
        var today = report.NewRecommended.Concat(report.NewOther).ToList();
        if (today.Count == 0)
            return null;

        var top = today.Take(5).Select(j => $"{j.Score}/10 · {j.Title} – {j.Company}").ToList();
        var more = today.Count - top.Count;

        return Build(user.Email,
            $"Job Scout: {today.Count} {CzechJobs(today.Count)} / {today.Count} new {(today.Count == 1 ? "job" : "jobs")}",
            [
                $"Dobrý den, {user.DisplayName},",
                $"dnes ráno jsme pro vás našli a ohodnotili {today.Count} {CzechJobs(today.Count)}"
                    + (report.NewRecommended.Count > 0 ? $", z toho {report.NewRecommended.Count} se shodou 7/10 a více." : "."),
                .. top,
                more > 0 ? $"… a dalších {more}." : "",
                "Ranní e-mail můžete vypnout na stránce Profil.",
            ],
            [
                $"Hello {user.DisplayName},",
                $"this morning we found and rated {today.Count} new {(today.Count == 1 ? "job" : "jobs")} for you"
                    + (report.NewRecommended.Count > 0 ? $", {report.NewRecommended.Count} of them with a match of 7/10 or more." : "."),
                more > 0 ? $"See all {today.Count} in the app." : "",
                "You can turn the morning e-mail off on the Profile page.",
            ],
            ("Otevřít nabídky / Open jobs", $"{appUrl}/matches"));
    }

    private static string CzechJobs(int n) => n == 1 ? "novou nabídku" : n is >= 2 and <= 4 ? "nové nabídky" : "nových nabídek";

    private static EmailMessage Build(string to, string subject, IEnumerable<string> czech, IEnumerable<string> english,
        (string Label, string Url) button)
    {
        var cs = czech.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        var en = english.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

        var text = new StringBuilder();
        cs.ForEach(l => text.AppendLine(l));
        text.AppendLine().AppendLine(button.Url).AppendLine().AppendLine("— English —").AppendLine();
        en.ForEach(l => text.AppendLine(l));
        text.AppendLine().AppendLine("Job Scout · https://jobscout.stanislavmatzke.com");

        var html = new StringBuilder();
        html.Append("""<div style="font-family:Segoe UI,Arial,sans-serif;font-size:15px;line-height:1.5;color:#1a2230;max-width:560px">""");
        html.Append("""<p style="font-weight:600;color:#0f6cbd;font-size:17px;margin:0 0 16px">Job Scout</p>""");
        cs.ForEach(l => html.Append($"<p style=\"margin:0 0 10px\">{Encode(l)}</p>"));
        html.Append($"""<p style="margin:20px 0"><a href="{Encode(button.Url)}" style="background:#0f6cbd;color:#fff;padding:10px 18px;border-radius:8px;text-decoration:none;font-weight:600">{Encode(button.Label)}</a></p>""");
        html.Append("""<hr style="border:0;border-top:1px solid #e1e5eb;margin:24px 0">""");
        en.ForEach(l => html.Append($"<p style=\"margin:0 0 10px;color:#5a6576\">{Encode(l)}</p>"));
        html.Append("</div>");

        return new EmailMessage(to, subject, text.ToString(), html.ToString());
    }

    private static string Encode(string s) => WebUtility.HtmlEncode(s);
}
