using JobScout.Application.Llm;
using JobScout.Application.Profiles;
using JobScout.Core.Domain;

namespace JobScout.Application.Letters;

/// <summary>Letter = null when the AI's answer is unusable (cut off by the token limit, or empty).</summary>
public sealed record CoverLetterDraft(string? Letter, LlmResponse Response);

/// <summary>One cover letter for one evaluated job. Used by the morning run and by the "Napsat dopis" button.</summary>
public sealed class CoverLetterWriter(ILlmClient llm)
{
    public async Task<CoverLetterDraft> WriteAsync(CandidateProfile profile, JobMatch match, GrammaticalGender gender, CancellationToken ct = default)
    {
        var response = await llm.CompleteAsync(BuildRequest(profile, match, gender), ct);   // an API outage still throws
        var letter = response.Text.Trim();

        return new CoverLetterDraft(
            response.StopReason == "max_tokens" || letter.Length == 0 ? null : letter,
            response);
    }

    private static LlmRequest BuildRequest(CandidateProfile profile, JobMatch match, GrammaticalGender gender)
    {
        var offer = match.JobOffer!;
        return new LlmRequest(
            CoverLetterPrompts.System,
            $$"""
              <profil_kandidata>
              {{LlmJson.Serialize(profile)}}
              </profil_kandidata>

              <rod>{{GenderText(gender)}}</rod>

              <hodnoceni>
              {{match.EvaluationJson}}
              </hodnoceni>

              <nabidka>
              Pozice: {{offer.Title}}
              Firma: {{offer.Company}}
              Místo: {{offer.Location}}

              {{offer.DetailText}}
              </nabidka>

              Napiš návrh průvodního dopisu.
              """)
        {
            MaxTokens = 2000,
            Tier = LlmTier.Smart,
            Purpose = LlmPurpose.CoverLetter
        };
    }

    private static string GenderText(GrammaticalGender gender) => gender switch
    {
        GrammaticalGender.Masculine => "mužský",
        GrammaticalGender.Feminine => "ženský",
        _ => "neuvedený"
    };

}

