using JobScout.Application.Llm;

namespace JobScout.Application.Evaluation;

public static class JobEvaluationJson
{
    /// <summary>Old or broken JSON must not break a list or a report – the job is shown without the details.</summary>
    public static JobEvaluation? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try { return LlmJson.Deserialize<JobEvaluation>(json); }
        catch (System.Text.Json.JsonException) { return null; }
    }
}
