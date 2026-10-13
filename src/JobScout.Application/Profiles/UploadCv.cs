using System.Text;
using JobScout.Application.Abstractions;
using JobScout.Application.Limits;
using JobScout.Application.Llm;
using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Profiles;

/// <summary>
/// New CV from the UI → analysis (only if it differs). The notes from the last version stay.
/// A new analysis is limited per user and day (ADR-0021); an unchanged CV costs nothing and is not counted.
/// </summary>
public sealed class UploadCv(IAppDbContext db, AnalyzeProfile analyze, AiQuota? quota = null)
{
    /// <summary>Extension → what we accept. Claude reads PDF and text; Word is turned into text here (it can't read .docx).</summary>
    private static readonly Dictionary<string, string> MediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".docx"] = DocxText.MediaType,
        [".md"] = "text/plain",
        [".txt"] = "text/plain",
    };

    public const string Formats = "Podporované formáty CV: PDF, Word (.docx), Markdown (.md), text (.txt).";

    public async Task<AnalyzedProfile> ExecuteAsync(Guid userId, string fileName, byte[] content, CancellationToken ct = default)
    {
        fileName = Path.GetFileName(fileName);              // never trust a path coming from the browser
        if (!MediaTypes.TryGetValue(Path.GetExtension(fileName), out var mediaType))
            throw new ArgumentException(Path.GetExtension(fileName).Equals(".doc", StringComparison.OrdinalIgnoreCase)
                ? "Starý formát Word (.doc) neumíme přečíst – uložte prosím životopis jako .docx nebo PDF."
                : Formats, nameof(fileName));
        if (content.Length == 0 || content.Length > StoredCv.MaxBytes)
            throw new ArgumentException($"CV musí mít nejvýš {StoredCv.MaxBytes / 1024 / 1024} MB a nesmí být prázdné.", nameof(content));
        if (mediaType == "application/pdf" && !content.AsSpan().StartsWith("%PDF-"u8))
            throw new ArgumentException("Soubor má příponu .pdf, ale nejde o platný dokument PDF.", nameof(content));

        // Word → plain text before anything else: that text is what is stored, hashed and sent to the AI
        // (the same .docx again = the same text = no new analysis)
        if (mediaType == DocxText.MediaType)
            (mediaType, content) = ("text/plain", Encoding.UTF8.GetBytes(DocxText.Extract(content)));

        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);
        var notes = await db.UserProfiles
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.Version)
            .Select(p => p.Notes)
            .FirstOrDefaultAsync(ct);

        // Checked before the CV is stored: a refused upload changes nothing
        using var slot = quota is not null && await analyze.WouldCallAiAsync(userId, content, notes, ct)
            ? await quota.EnterAsync(userId, AiAction.Profile, ct)
            : null;

        return await analyze.ExecuteAsync(new ProfileInput(
            user.Provider, user.Subject, user.DisplayName, fileName, new LlmDocument("CV", mediaType, content), notes), ct);
    }
}
