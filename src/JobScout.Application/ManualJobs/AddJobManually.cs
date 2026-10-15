using System.Net;
using System.Text;
using JobScout.Application.Abstractions;
using JobScout.Application.Evaluation;
using JobScout.Application.Jobs;
using JobScout.Application.Limits;
using JobScout.Application.Llm;
using JobScout.Application.Profiles;
using JobScout.Core.Domain;
using JobScout.Core.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace JobScout.Application.ManualJobs;

public sealed record ManualJobFile(string FileName, byte[] Content);

/// <summary>Exactly one of Text, Url, File.</summary>
/// <param name="Status">Where the job already is – e.g. Applied for an ad the user answered before adding it.</param>
/// <param name="Evaluate">Have it evaluated straight away (otherwise later with the button in the detail).</param>
public sealed record AddJobInput(
    string? Text = null, string? Url = null, ManualJobFile? File = null,
    MatchStatus Status = MatchStatus.New, bool Evaluate = true);

public enum AddJobStatus { Added, AlreadyInList, NotAJobAd, NoProfile, PageUnavailable, SourceUnavailable, AiFailed }

/// <param name="MatchId">The new job – or the one already in the list (AlreadyInList).</param>
/// <param name="Message">Why the page could not be read (PageUnavailable) – for the user, in Czech.</param>
public sealed record AddJobResult(AddJobStatus Status, Guid? MatchId = null, bool Evaluated = false, string? Message = null);

/// <summary>
/// "Přidat nabídku" (ADR-0013): a job ad that came from elsewhere – pasted text, a file (PDF, Word, text, a saved e-mail)
/// or a link. A link to a portal we know (jobs.cz) becomes the normal public ad; anything else is read by the AI into
/// a private ad only this user sees. Then, if asked, the normal evaluation. Nothing is evaluated by the morning run –
/// the user decides when to spend the AI. Limited per user and day (ADR-0021) – checked before anything is called.
/// </summary>
public sealed class AddJobManually(
    IAppDbContext db,
    IEnumerable<IJobSource> sources,
    JobAdExtractor extractor,
    MatchEvaluator evaluator,
    IWebPageReader pages,
    IEmailReader emails,
    TimeProvider time,
    ILogger<AddJobManually> logger,
    AiQuota? quota = null)
{
    /// <summary>The same size as a CV – a job ad is never bigger.</summary>
    public const int MaxFileBytes = StoredCv.MaxBytes;

    /// <summary>≈ 15 pages of text; a pasted ad longer than this is surely something else.</summary>
    public const int MaxTextChars = 50_000;

    public const string Formats = "Podporované soubory: PDF, Word (.docx), text (.txt, .md) a uložený e-mail (.eml).";

    private const int MaxAttachments = 5;

    public async Task<AddJobResult> ExecuteAsync(Guid userId, AddJobInput input, CancellationToken ct = default)
    {
        var url = Validate(input);
        var current = await db.CurrentProfileAsync(userId, ct);
        if (input.Evaluate && current is null)
            return new AddJobResult(AddJobStatus.NoProfile);

        // A link to a portal we search anyway: the public ad, no AI needed to read it
        (IJobSource? Source, string? Id) known = url is null
            ? default
            : sources.Select(s => (Source: s, Id: s.ExternalIdFromUrl(url))).FirstOrDefault(x => x.Id is not null);

        // One click at a time, limits checked before anything is read, called or saved (ADR-0021)
        var action = known.Source is null ? AiAction.JobImport : input.Evaluate ? AiAction.Evaluation : (AiAction?)null;
        using var slot = quota is null || action is null ? null : await quota.EnterAsync(userId, action.Value, ct);
        if (slot is not null && action == AiAction.JobImport && input.Evaluate
            && await quota!.CheckAsync(userId, AiAction.Evaluation, ct) is { } denial)
            throw new AiLimitExceededException(denial);
        using var usage = LlmCallContext.Begin(userId);      // the costs belong to the user, not to a morning run

        var added = known.Source is not null
            ? await FromSourceAsync(userId, known.Source, known.Id!, input.Status, current, ct)
            : await FromMaterialAsync(userId, input, url, current, ct);
        if (added.Status != AddJobStatus.Added || !input.Evaluate)
            return added;

        return added with { Evaluated = await TryEvaluateAsync(added.MatchId!.Value, current!, ct) };
    }

    // ---------- input ----------

    /// <returns>the link, when the input is one</returns>
    private static Uri? Validate(AddJobInput input)
    {
        var given = new[] { !string.IsNullOrWhiteSpace(input.Text), !string.IsNullOrWhiteSpace(input.Url), input.File is not null }
            .Count(x => x);
        if (given != 1)
            throw new ArgumentException("Vložte buď text inzerátu, nebo odkaz, nebo soubor – právě jedno z nich.");
        if (!Enum.IsDefined(input.Status) || input.Status == MatchStatus.Hidden)
            throw new ArgumentException("Neplatný stav nabídky.", nameof(input));
        if (input.Text?.Length > MaxTextChars)
            throw new ArgumentException($"Text inzerátu může mít nejvýš {MaxTextChars} znaků.", nameof(input));
        if (input.File is { } file && (file.Content.Length == 0 || file.Content.Length > MaxFileBytes))
            throw new ArgumentException($"Soubor musí mít nejvýš {MaxFileBytes / 1024 / 1024} MB a nesmí být prázdný.", nameof(input));

        if (string.IsNullOrWhiteSpace(input.Url))
            return null;
        if (!Uri.TryCreate(input.Url.Trim(), UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https")
            || input.Url.Length > 2000)
            throw new ArgumentException("Odkaz musí být celá webová adresa začínající http:// nebo https://.", nameof(input));
        return url;
    }

    // ---------- a portal we know ----------

    private async Task<AddJobResult> FromSourceAsync(
        Guid userId, IJobSource source, string externalId, MatchStatus status, CurrentProfile? current, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var offer = await db.JobOffers.SingleOrDefaultAsync(o => o.Source == source.Name && o.ExternalId == externalId, ct);
        if (offer is not null)
        {
            var mine = await db.JobMatches
                .Where(m => m.UserId == userId && m.JobOfferId == offer.Id)
                .Select(m => (Guid?)m.Id)
                .SingleOrDefaultAsync(ct);
            if (mine is not null)
                return new AddJobResult(AddJobStatus.AlreadyInList, mine);
        }
        else
        {
            JobDetail detail;
            try
            {
                detail = await source.GetDetailAsync(externalId, ct);
            }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                return new AddJobResult(AddJobStatus.PageUnavailable, Message: "Inzerát již na portálu není k dispozici.");
            }
            catch (HttpRequestException)
            {
                return new AddJobResult(AddJobStatus.SourceUnavailable);
            }

            // The detail page has no location – the same shared offer the search would create, only without it
            offer = JobOffer.FromListing(new JobListing(source.Name, externalId, detail.Title, detail.Company, "", detail.Url, []), now);
            offer.SetDetail(detail, now);
            db.JobOffers.Add(offer);
        }

        return await SaveAsync(userId, offer, status, current, now, ct);
    }

    // ---------- anything else: the AI reads it ----------

    private sealed record Material(string? Text, IReadOnlyList<LlmDocument> Documents, string Url);

    private async Task<AddJobResult> FromMaterialAsync(
        Guid userId, AddJobInput input, Uri? url, CurrentProfile? current, CancellationToken ct)
    {
        Material material;
        if (url is not null)
        {
            try
            {
                var page = await pages.ReadAsync(url, ct);
                var text = string.IsNullOrWhiteSpace(page.Title) ? page.Text : $"{page.Title}\n\n{page.Text}";
                material = new Material(Clip(text, MaxTextChars), page.Document is null ? [] : [page.Document], page.Url.ToString());
            }
            catch (WebPageUnavailableException ex)
            {
                logger.LogInformation(ex, "Stránku {Url} se nepodařilo načíst", url);
                return new AddJobResult(AddJobStatus.PageUnavailable, Message: ex.Message);
            }
        }
        else if (input.File is { } file)
            material = FromFile(file);
        else
            material = new Material(input.Text!.Trim(), [], "");

        if (string.IsNullOrWhiteSpace(material.Text) && material.Documents.Count == 0)
            return new AddJobResult(AddJobStatus.NotAJobAd);

        ExtractedJobAd ad;
        try
        {
            ad = (await extractor.ExtractAsync(material.Text, material.Documents, ct)).Ad;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The AI is down or its answer was cut off – nothing was saved, the user can simply try again
            logger.LogWarning(ex, "Ručně vložený inzerát se nepodařilo přečíst");
            return new AddJobResult(AddJobStatus.AiFailed);
        }
        var adText = ad.Text ?? material.Text;
        if (!ad.IsJobAd || string.IsNullOrWhiteSpace(adText))
            return new AddJobResult(AddJobStatus.NotAJobAd);

        var now = time.GetUtcNow();
        var offer = JobOffer.AddedByUser(userId,
            title: Clip(ad.Title, 500) is { Length: > 0 } title ? title : "Nabídka bez názvu",
            company: Clip(ad.Company, 300), location: Clip(ad.Location, 300),
            url: material.Url.Length <= 500 ? material.Url : "", text: adText.Trim(), now);
        db.JobOffers.Add(offer);
        return await SaveAsync(userId, offer, input.Status, current, now, ct);
    }

    private Material FromFile(ManualJobFile file)
    {
        var name = Path.GetFileName(file.FileName);                 // never trust a path coming from the browser
        switch (Path.GetExtension(name).ToLowerInvariant())
        {
            case ".pdf":
                return new Material(null, [Pdf(name, file.Content)], "");
            case ".docx":
                return new Material(Clip(DocxText.Extract(file.Content), MaxTextChars), [], "");
            case ".txt" or ".md":
                return new Material(Clip(Encoding.UTF8.GetString(file.Content), MaxTextChars), [], "");
            case ".eml":
                return FromEmail(emails.Read(file.Content));
            case ".msg":
                throw new ArgumentException("E-mail z Outlooku (.msg) neumíme přečíst – uložte ho prosím jako .eml, nebo zkopírujte jeho text.");
            default:
                throw new ArgumentException(Formats);
        }
    }

    /// <summary>The headers that matter + the body; attachments the way a file would be read (a PDF for the AI, the rest as text).</summary>
    private static Material FromEmail(EmailMessage email)
    {
        var text = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(email.From)) text.AppendLine($"Od: {email.From}");
        if (email.Date is { } date) text.AppendLine($"Datum: {date:d. M. yyyy}");
        if (!string.IsNullOrWhiteSpace(email.Subject)) text.AppendLine($"Předmět: {email.Subject}");
        text.AppendLine().AppendLine(email.Text.Trim());

        var documents = new List<LlmDocument>();
        foreach (var attachment in email.Attachments.Take(MaxAttachments))
        {
            var name = Path.GetFileName(attachment.FileName);
            switch (Path.GetExtension(name).ToLowerInvariant())
            {
                case ".pdf" when attachment.Content.AsSpan().StartsWith("%PDF-"u8):
                    documents.Add(new LlmDocument($"Příloha {name}", "application/pdf", attachment.Content));
                    break;
                case ".docx":
                    try
                    {
                        text.AppendLine().AppendLine($"--- Příloha {name} ---").AppendLine(DocxText.Extract(attachment.Content));
                    }
                    catch (ArgumentException)
                    {
                        // an unreadable attachment must not spoil the e-mail itself
                    }
                    break;
                case ".txt" or ".md":
                    text.AppendLine().AppendLine($"--- Příloha {name} ---").AppendLine(Encoding.UTF8.GetString(attachment.Content));
                    break;
            }
        }

        return new Material(Clip(text.ToString(), MaxTextChars), documents, "");
    }

    private static LlmDocument Pdf(string name, byte[] content) =>
        content.AsSpan().StartsWith("%PDF-"u8)
            ? new LlmDocument($"Inzerát {name}", "application/pdf", content)
            : throw new ArgumentException("Soubor má příponu .pdf, ale nejde o platný dokument PDF.");

    // ---------- saving and evaluating ----------

    private async Task<AddJobResult> SaveAsync(
        Guid userId, JobOffer offer, MatchStatus status, CurrentProfile? current, DateTimeOffset now, CancellationToken ct)
    {
        var match = JobMatch.AddedByUser(userId, offer.Id, current?.Version ?? 0, now);
        if (status != MatchStatus.New)
            match.ChangeStatus(status, null, now);                  // recorded in the history like any other change
        db.JobMatches.Add(match);
        await db.SaveChangesAsync(ct);                              // the job is kept even if the evaluation fails
        return new AddJobResult(AddJobStatus.Added, match.Id);
    }

    /// <returns>false = not evaluated now (the portal or the AI failed) – the button in the detail can do it later</returns>
    private async Task<bool> TryEvaluateAsync(Guid matchId, CurrentProfile current, CancellationToken ct)
    {
        var match = await db.JobMatches.Include(m => m.JobOffer).SingleAsync(m => m.Id == matchId, ct);
        try
        {
            var outcome = await evaluator.EvaluateAsync(match, current.Profile, current.Version, ct);
            await db.SaveChangesAsync(ct);
            return outcome.Status == MatchEvaluationStatus.Evaluated;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Ručně přidanou nabídku {MatchId} se nepodařilo ohodnotit", matchId);
            return false;
        }
    }

    private static string Clip(string? text, int max)
    {
        text = text?.Trim() ?? "";
        return text.Length <= max ? text : text[..max];
    }
}
