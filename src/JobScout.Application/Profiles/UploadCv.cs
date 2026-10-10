using JobScout.Application.Abstractions;
using JobScout.Application.Llm;
using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Profiles;

/// <summary>New CV from the UI → analysis (only if it differs). The notes from the last version stay.</summary>
public sealed class UploadCv(IAppDbContext db, AnalyzeProfile analyze)
{
    /// <summary>Extension → what Claude gets. Markdown and text go as plain text; Word may come later.</summary>
    private static readonly Dictionary<string, string> MediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".md"] = "text/plain",
        [".txt"] = "text/plain",
    };

    public async Task<AnalyzedProfile> ExecuteAsync(Guid userId, string fileName, byte[] content, CancellationToken ct = default)
    {
        fileName = Path.GetFileName(fileName);              // never trust a path coming from the browser
        if (!MediaTypes.TryGetValue(Path.GetExtension(fileName), out var mediaType))
            throw new ArgumentException("Podporované formáty CV: PDF, Markdown (.md), text (.txt).", nameof(fileName));
        if (content.Length == 0 || content.Length > StoredCv.MaxBytes)
            throw new ArgumentException($"CV musí mít nejvýš {StoredCv.MaxBytes / 1024 / 1024} MB a nesmí být prázdné.", nameof(content));
        if (mediaType == "application/pdf" && !content.AsSpan().StartsWith("%PDF-"u8))
            throw new ArgumentException("Soubor má příponu .pdf, ale nejde o platný dokument PDF.", nameof(content));

        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);
        var notes = await db.UserProfiles
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.Version)
            .Select(p => p.Notes)
            .FirstOrDefaultAsync(ct);

        return await analyze.ExecuteAsync(new ProfileInput(
            user.Provider, user.Subject, user.DisplayName, fileName, new LlmDocument("CV", mediaType, content), notes), ct);
    }
}
