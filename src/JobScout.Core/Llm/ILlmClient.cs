namespace JobScout.Core.Llm;

public interface ILlmClient
{
    Task<LlmResponse> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);
}

public sealed record LlmResponse(
    string Text,
    string Model,
    string? StopReason,
    long InputTokens,
    long OutputTokens);