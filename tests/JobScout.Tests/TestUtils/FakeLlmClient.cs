using JobScout.Application.Llm;

namespace JobScout.Tests.TestUtils;

/// <summary>
/// Fake AI: returns a prepared answer and remembers every request – so tests can check
/// how many times the (paid) model would have been called and with what. No network, no tokens.
/// </summary>
internal sealed class FakeLlmClient(Func<LlmRequest, LlmResponse> respond) : ILlmClient
{
    public List<LlmRequest> Requests { get; } = [];
    public int Calls => Requests.Count;

    /// <summary>Always answers with the same JSON text.</summary>
    public FakeLlmClient(string json, string? stopReason = "end_turn")
        : this(_ => new LlmResponse(json, "fake-model", stopReason, InputTokens: 100, OutputTokens: 50)) { }

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        Requests.Add(request);
        return Task.FromResult(respond(request));
    }
}
