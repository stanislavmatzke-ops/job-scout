using JobScout.Core.Llm;
using JobScout.Core.Profile;

namespace JobScout.Core.Evaluation;

public sealed record JobEvaluationResult(JobEvaluation Evaluation, LlmResponse Response);

public sealed class JobEvaluator(ILlmClient llm)
{
    public async Task<JobEvaluationResult> EvaluateAsync(CandidateProfile profile, string jobText, CancellationToken ct = default)
    {
        var request = new LlmRequest(
            EvaluationPrompts.System,
            $"""
            <profil_kandidata>
            {LlmJson.Serialize(profile)}
            </profil_kandidata>

            <nabidka>
            {jobText}
            </nabidka>

            Ohodnoť nabídku vůči profilu kandidáta.
            """)
        {
            JsonSchema = EvaluationPrompts.Schema,
            MaxTokens = 1500,
        };

        var response = await llm.CompleteAsync(request, ct);
        var evaluation = LlmJson.Parse<JobEvaluation>(response);

        // The schema can't express min/max – never trust LLM output blindly
        if (evaluation.Score is < 1 or > 10)
            throw new InvalidOperationException($"Skóre mimo rozsah: {evaluation.Score}");

        return new JobEvaluationResult(evaluation, response);
    }
}
