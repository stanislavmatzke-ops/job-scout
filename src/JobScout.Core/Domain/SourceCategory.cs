using System.Globalization;
using System.Text;

namespace JobScout.Core.Domain;

/// <summary>
/// Learned catalogue (ADR-0015): what a job source answered for a profession name – positive or negative.
/// Shared by all users, one row per (Source, Key). Re-checked after a while, portals add and drop professions.
/// </summary>
public sealed class SourceCategory
{
    public Guid Id { get; private set; }
    public string Source { get; private set; } = "";
    public string Key { get; private set; } = "";        // normalized name: "Lakýrník" → "lakyrnik"
    public string Label { get; private set; } = "";      // human-readable name
    public string? CategoryId { get; private set; }      // the source's id (jobs.cz slug); null = the source doesn't have it
    public DateTimeOffset CheckedAt { get; private set; }

    public bool Exists => CategoryId is not null;

    private SourceCategory() { }   // for EF Core

    public SourceCategory(string source, string label, string? categoryId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        var key = NormalizeKey(label);
        if (key.Length == 0)
            throw new ArgumentException("Profession name is empty.", nameof(label));

        Id = Guid.CreateVersion7(now);
        Source = source;
        Key = key;
        Label = label.Trim();
        CategoryId = categoryId;
        CheckedAt = now;
    }

    /// <summary>Asked the source again – the answer may have changed (profession added or removed).</summary>
    public void Recheck(string? categoryId, DateTimeOffset now)
    {
        CategoryId = categoryId;
        CheckedAt = now;
    }

    public bool IsStale(DateTimeOffset now, TimeSpan maxAge) => now - CheckedAt > maxAge;

    /// <summary>"  Zdravotní sestra " → "zdravotni-sestra": same name in any spelling = same row.</summary>
    public static string NormalizeKey(string label)
    {
        var decomposed = (label ?? "").Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetterOrDigit(c))
                sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '-')
                sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }
}