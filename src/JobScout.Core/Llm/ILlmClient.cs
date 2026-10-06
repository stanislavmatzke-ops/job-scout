namespace JobScout.Core.Llm;

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
}

/// <summary>A document for the model. Supported media types: application/pdf, text/plain.</summary>
public sealed record LlmDocument(string Title, string MediaType, byte[] Content);

public sealed record LlmResponse(
    string Text,
    string Model,
    string? StopReason,
    long InputTokens,
    long OutputTokens);
