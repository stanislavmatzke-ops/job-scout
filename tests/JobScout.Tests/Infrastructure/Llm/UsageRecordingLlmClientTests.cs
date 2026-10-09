using JobScout.Application.Llm;
using JobScout.Core.Domain;
using JobScout.Infrastructure.Claude;
using JobScout.Tests.TestUtils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JobScout.Tests.Infrastructure.Llm;

/// <summary>
/// Decorator around the real client (ADR-0016): every call – successful or not – is written as an LlmCall
/// in its own DbContext, so it survives a failed use case. Recording must never break the actual work.
/// </summary>
public sealed class UsageRecordingLlmClientTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 9, 6, 0, 0, TimeSpan.Zero);

    private readonly SqliteTestDatabase _database = new();
    private readonly FixedTimeProvider _time = new(T0);

    private static readonly IOptions<ClaudeOptions> Options = Microsoft.Extensions.Options.Options.Create(new ClaudeOptions
    {
        Pricing = new() { ["claude-sonnet-5-5"] = new() { InputPerMTok = 2m, OutputPerMTok = 10m } },
    });

    public void Dispose() => _database.Dispose();

    private UsageRecordingLlmClient Create(ILlmClient inner, bool dbFails = false) =>
        new(inner, new TestDbContextFactory(_database, dbFails), Options, _time, NullLogger<UsageRecordingLlmClient>.Instance);

    private static LlmRequest Request(LlmPurpose purpose = LlmPurpose.Evaluation) =>
        new("system", "user") { Purpose = purpose };

    private async Task<List<LlmCall>> CallsAsync()
    {
        await using var db = _database.CreateContext();
        return await db.LlmCalls.ToListAsync();
    }

    [Fact]
    public async Task Successful_call_is_recorded_with_purpose_tokens_and_cost()
    {
        var inner = new FakeLlmClient(_ => new LlmResponse("{}", "claude-sonnet-5-5", "end_turn", 5000, 700));

        var response = await Create(inner).CompleteAsync(Request(LlmPurpose.Evaluation));

        Assert.Equal("{}", response.Text);                       // the caller gets the real response
        var call = Assert.Single(await CallsAsync());
        Assert.True(call.Succeeded);
        Assert.Equal("Evaluation", call.Purpose);
        Assert.Equal("claude-sonnet-5-5", call.Model);
        Assert.Equal((5000L, 700L, 17_000L), (call.InputTokens, call.OutputTokens, call.CostMicroUsd));
        Assert.Equal(T0, call.At);
    }

    [Fact]
    public async Task Call_is_attributed_to_the_ambient_user_and_run()
    {
        var user = Guid.NewGuid();
        var run = Guid.NewGuid();
        var inner = new FakeLlmClient("{}");

        using (LlmCallContext.Begin(user, run))
            await Create(inner).CompleteAsync(Request());

        var call = Assert.Single(await CallsAsync());
        Assert.Equal((user, run), (call.UserId, call.ScoutRunId));
    }

    [Fact]
    public async Task Call_without_context_is_still_recorded()
    {
        await Create(new FakeLlmClient("{}")).CompleteAsync(Request());

        var call = Assert.Single(await CallsAsync());
        Assert.Null(call.UserId);
        Assert.Null(call.ScoutRunId);
    }

    [Fact]
    public async Task Unknown_model_is_recorded_with_zero_cost()
    {
        var inner = new FakeLlmClient(_ => new LlmResponse("{}", "some-new-model", "end_turn", 100, 10));

        await Create(inner).CompleteAsync(Request());

        Assert.Equal(0, Assert.Single(await CallsAsync()).CostMicroUsd);
    }

    [Fact]
    public async Task Failed_call_is_recorded_and_the_exception_rethrown()
    {
        var inner = new FakeLlmClient(_ => throw new HttpRequestException("529 Overloaded"));

        await Assert.ThrowsAsync<HttpRequestException>(() => Create(inner).CompleteAsync(Request(LlmPurpose.Triage)));

        var call = Assert.Single(await CallsAsync());
        Assert.False(call.Succeeded);
        Assert.Equal("Triage", call.Purpose);
        Assert.Contains("529 Overloaded", call.Error);
    }

    [Fact]
    public async Task Cancellation_is_not_recorded_as_a_failure()
    {
        var inner = new FakeLlmClient(_ => throw new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() => Create(inner).CompleteAsync(Request()));

        Assert.Empty(await CallsAsync());
    }

    [Fact]
    public async Task Database_failure_does_not_break_the_ai_call()
    {
        // Cost tracking is important, but never more important than the user's evaluation
        var inner = new FakeLlmClient("{\"ok\":true}");

        var response = await Create(inner, dbFails: true).CompleteAsync(Request());

        Assert.Equal("{\"ok\":true}", response.Text);
    }

    [Fact]
    public async Task Each_call_is_one_row()
    {
        var client = Create(new FakeLlmClient("{}"));

        await client.CompleteAsync(Request(LlmPurpose.Triage));
        await client.CompleteAsync(Request(LlmPurpose.Evaluation));
        await client.CompleteAsync(Request(LlmPurpose.CoverLetter));

        Assert.Equal(new[] { "CoverLetter", "Evaluation", "Triage" }, (await CallsAsync()).Select(c => c.Purpose).Order().ToArray());
    }
}
