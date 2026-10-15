namespace JobScout.Application.Llm;

public enum LlmTier
{
    Fast,
    Smart
}

/// <summary>What the call is for – the cost report groups by it.</summary>
public enum LlmPurpose { Other, Profile, SearchPlan, Triage, Evaluation, CoverLetter, JobImport }

public interface ILlmClient
{
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default);
}

public sealed record LlmRequest(string SystemPrompt, string UserPrompt)
{
    /// <summary>When set, the response is JSON matching this schema.</summary>
    public string? JsonSchema { get; init; }

    /// <summary>Overrides the default MaxTokens from configuration.</summary>
    public int? MaxTokens { get; init; }

    /// <summary>Documents (e.g. a PDF CV) sent to the model before the user prompt.</summary>
    public IReadOnlyList<LlmDocument> Documents { get; init; } = [];

    /// <summary>Model class – the concrete model is chosen in configuration.</summary>
    public LlmTier Tier { get; init; } = LlmTier.Smart;
    
    /// <summary>What the call is for – recorded with its cost (ADR-0016).</summary>
    public LlmPurpose Purpose { get; init; } = LlmPurpose.Other;
}

/// <summary>A document for the model. Supported media types: application/pdf, text/plain.</summary>
public sealed record LlmDocument(string Title, string MediaType, byte[] Content);

public sealed record LlmResponse(
    string Text,
    string Model,
    string? StopReason,
    long InputTokens,
    long OutputTokens);