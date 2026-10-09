using JobScout.Core.Domain;

namespace JobScout.Tests.Core.Domain;

/// <summary>One paid (or failed) call to the model – the unit of cost tracking (ADR-0016).</summary>
public class LlmCallTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 9, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Successful_call_keeps_tokens_cost_and_context()
    {
        var userId = Guid.NewGuid();
        var runId = Guid.NewGuid();

        var call = LlmCall.Success(userId, runId, "Evaluation", "claude-sonnet-5-5", 5000, 700, 17_000, 6500, T0);

        Assert.True(call.Succeeded);
        Assert.Equal((userId, runId), (call.UserId, call.ScoutRunId));
        Assert.Equal(("Evaluation", "claude-sonnet-5-5"), (call.Purpose, call.Model));
        Assert.Equal((5000L, 700L), (call.InputTokens, call.OutputTokens));
        Assert.Equal(17_000, call.CostMicroUsd);
        Assert.Equal(0.017m, call.CostUsd);
        Assert.Equal(6500, call.DurationMs);
        Assert.Equal(T0, call.At);
        Assert.Null(call.Error);
    }

    [Fact]
    public void Failed_call_has_no_tokens_and_keeps_the_error()
    {
        var call = LlmCall.Failure(null, null, "Triage", "Fast", "HttpRequestException: 529 Overloaded", 1200, T0);

        Assert.False(call.Succeeded);
        Assert.Equal(0, call.InputTokens + call.OutputTokens + call.CostMicroUsd);
        Assert.Equal("HttpRequestException: 529 Overloaded", call.Error);
        Assert.Null(call.UserId);
    }

    [Fact]
    public void Long_error_is_truncated()
    {
        var call = LlmCall.Failure(null, null, "Triage", "Fast", new string('x', 5000), 0, T0);

        Assert.Equal(LlmCall.MaxErrorLength, call.Error!.Length);
    }
}
