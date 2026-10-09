using JobScout.Application.Llm;

namespace JobScout.Tests.Application.Llm;

/// <summary>Ambient "who and which run is this AI call for" – flows into awaited calls, restored on dispose.</summary>
public class LlmCallContextTests
{
    [Fact]
    public void Nothing_is_set_by_default()
    {
        Assert.Null(LlmCallContext.Current);
    }

    [Fact]
    public void Begin_sets_and_dispose_restores()
    {
        var user = Guid.NewGuid();

        using (LlmCallContext.Begin(user))
            Assert.Equal(user, LlmCallContext.Current!.UserId);

        Assert.Null(LlmCallContext.Current);
    }

    [Fact]
    public void Nested_scope_restores_the_outer_one()
    {
        var user = Guid.NewGuid();
        var run = Guid.NewGuid();

        using (LlmCallContext.Begin(user))
        {
            using (LlmCallContext.Begin(user, run))
                Assert.Equal(run, LlmCallContext.Current!.ScoutRunId);

            Assert.Null(LlmCallContext.Current!.ScoutRunId);
            Assert.Equal(user, LlmCallContext.Current.UserId);
        }
    }

    [Fact]
    public async Task Context_flows_into_awaited_calls()
    {
        var user = Guid.NewGuid();
        Guid? seen = null;

        using (LlmCallContext.Begin(user))
        {
            await Task.Yield();
            await Task.Run(() => seen = LlmCallContext.Current?.UserId);
        }

        Assert.Equal(user, seen);
    }
}
