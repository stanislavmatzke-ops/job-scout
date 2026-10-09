using JobScout.Application.Llm;

namespace JobScout.Application.Profiles;

public sealed record ProfileExtractionResult(CandidateProfile Profile, LlmResponse Response);

/// <summary>
/// CV (PDF or text) + optional free-text notes → structured <see cref="CandidateProfile"/>.
/// In the app this runs once when the user uploads a CV, not for every job.
/// </summary>
public sealed class ProfileExtractor(ILlmClient llm)
{
    public async Task<ProfileExtractionResult> ExtractAsync(LlmDocument cv, string? notes, CancellationToken ct = default)
    {
        var notesText = string.IsNullOrWhiteSpace(notes) ? "(žádné)" : notes.Trim();

        var request = new LlmRequest(
            ProfilePrompts.System,
            $"""
            <poznamky_kandidata>
            {notesText}
            </poznamky_kandidata>

            Vytvoř profil kandidáta z přiloženého CV a jeho poznámek.
            """)
        {
            Documents = [cv],
            JsonSchema = ProfilePrompts.Schema,
            MaxTokens = 2000,
            Purpose = LlmPurpose.Profile
        };

        var response = await llm.CompleteAsync(request, ct);
        return new ProfileExtractionResult(LlmJson.Parse<CandidateProfile>(response), response);
    }
}
