using JobScout.Application.Llm;

namespace JobScout.Application.Limits;

/// <summary>What a user can start by clicking – each one is a paid AI call.</summary>
public enum AiAction { Profile, CoverLetter, Evaluation }

/// <summary>Which limit was hit – the UI words the message by it (Czech / English).</summary>
public enum AiLimitReason { Profile, CoverLetter, Evaluation, LetterRewrites, UserCost, AppPaused }

/// <param name="Limit">The number that was reached (null for the money limits).</param>
/// <param name="RetryAt">When it frees up again (null = not by waiting: rewrites per job, or unknown).</param>
public sealed record AiLimitDenial(AiLimitReason Reason, int? Limit, DateTimeOffset? RetryAt);

/// <summary>Thrown by the use cases before any AI call or any change – the API turns it into 429 Too Many Requests.</summary>
public sealed class AiLimitExceededException(AiLimitDenial denial)
    : Exception($"AI limit reached: {denial.Reason}")
{
    public AiLimitDenial Denial { get; } = denial;
}

/// <summary>One counted limit: used / allowed within the window, and when a slot frees up again.</summary>
public sealed record AiLimitUsage(int Used, int Limit, DateTimeOffset? ResetsAt)
{
    public int Left => Math.Max(0, Limit - Used);
}

/// <summary>GET /api/me/limits – the UI shows "2 of 3 left today" next to the buttons.</summary>
public sealed record AiLimitsView(
    bool Unlimited,
    AiLimitUsage Profile, AiLimitUsage CoverLetters, AiLimitUsage Evaluations,
    int LetterRewritesPerJob,
    decimal CostUsd, decimal CostLimitUsd, DateTimeOffset? CostResetsAt,
    bool Paused);

internal static class AiActions
{
    /// <summary>The LlmCall.Purpose each click is recorded with (ADR-0016).</summary>
    public static string Purpose(this AiAction action) => action switch
    {
        AiAction.Profile => nameof(LlmPurpose.Profile),
        AiAction.CoverLetter => nameof(LlmPurpose.CoverLetter),
        AiAction.Evaluation => nameof(LlmPurpose.Evaluation),
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

    public static AiLimitReason Reason(this AiAction action) => action switch
    {
        AiAction.Profile => AiLimitReason.Profile,
        AiAction.CoverLetter => AiLimitReason.CoverLetter,
        _ => AiLimitReason.Evaluation,
    };
}
