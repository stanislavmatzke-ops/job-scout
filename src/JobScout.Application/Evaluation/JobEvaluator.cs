using JobScout.Application.Llm;
using JobScout.Application.Profiles;

using JobScout.Application.Jobs;

namespace JobScout.Application.Evaluation;

public sealed record JobEvaluationResult(JobEvaluation Evaluation, LlmResponse Response);

public sealed class JobEvaluator(ILlmClient llm)
{
    public async Task<JobEvaluationResult> EvaluateAsync(CandidateProfile profile, string jobText, CancellationToken ct = default)
    {
        var language = AdLanguageDetector.Detect(jobText);   // decided here, not by the model (see AdLanguageDetector)
        var request = new LlmRequest(
            EvaluationPrompts.System,
            $"""
            <profil_kandidata>
            {LlmJson.Serialize(profile)}
            </profil_kandidata>

            <nabidka>
            {jobText}
            </nabidka>

            <jazyk_vystupu>{language.PromptName()}</jazyk_vystupu>

            Ohodnoť nabídku vůči profilu kandidáta. Texty hodnocení piš v jazyce z <jazyk_vystupu>.
            """)
        {
            JsonSchema = EvaluationPrompts.Schema,
            MaxTokens = 1500,
            Purpose = LlmPurpose.Evaluation
        };

        var response = await llm.CompleteAsync(request, ct);
        var evaluation = LlmJson.Parse<JobEvaluation>(response);

        // The schema can't express min/max – never trust LLM output blindly
        if (evaluation.Score is < 1 or > 10)
            throw new InvalidOperationException($"Skóre mimo rozsah: {evaluation.Score}");

        return new JobEvaluationResult(evaluation, response);
    }
}
