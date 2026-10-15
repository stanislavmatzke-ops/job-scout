using JobScout.Application.Llm;

namespace JobScout.Application.ManualJobs;

/// <summary>What the AI read from the user's material. Text is null when we already had the ad as text.</summary>
public sealed record ExtractedJobAd(bool IsJobAd, string Title, string Company, string Location, string? Text = null);

public sealed record JobAdExtractionResult(ExtractedJobAd Ad, LlmResponse Response);

/// <summary>
/// Pasted text, an e-mail, a PDF or a web page → the same shape as a scraped ad (title, company, location, text).
/// Cheap model: it only reads and copies, the judging is done later by the evaluation.
/// </summary>
public sealed class JobAdExtractor(ILlmClient llm)
{
    /// <param name="text">The material as text (pasted, from an e-mail or a web page) – may be empty when there are documents.</param>
    /// <param name="documents">Material only the model can read (a PDF) – then it also writes the ad's text for us.</param>
    public async Task<JobAdExtractionResult> ExtractAsync(string? text, IReadOnlyList<LlmDocument> documents, CancellationToken ct = default)
    {
        var withText = documents.Any(d => d.MediaType != "text/plain");
        var material = string.IsNullOrWhiteSpace(text) ? "(viz přiložené dokumenty)" : text.Trim();

        var request = new LlmRequest(
            withText ? JobAdPrompts.System + JobAdPrompts.TextRule : JobAdPrompts.System,
            $"""
            <podklady>
            {material}
            </podklady>

            Vytáhni údaje o nabídce práce z podkladů{(documents.Count > 0 ? " a přiložených dokumentů" : "")}.
            """)
        {
            Documents = documents,
            JsonSchema = JobAdPrompts.Schema(withText),
            MaxTokens = withText ? 8000 : 500,
            Tier = LlmTier.Fast,
            Purpose = LlmPurpose.JobImport
        };

        var response = await llm.CompleteAsync(request, ct);
        var ad = LlmJson.Parse<ExtractedJobAd>(response);
        return new JobAdExtractionResult(withText ? ad : ad with { Text = null }, response);
    }
}
