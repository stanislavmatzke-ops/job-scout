using JobScout.Infrastructure.Claude;

namespace JobScout.Tests.Infrastructure.Llm;

/// <summary>Tokens → micro-dollars (whole numbers: exact and summable in SQL, unlike decimals in SQLite).</summary>
public class LlmPricingTests
{
    private static readonly Dictionary<string, ModelPrice> Prices = new()
    {
        ["claude-haiku-4-5"] = new() { InputPerMTok = 1m, OutputPerMTok = 5m },
        ["claude-sonnet-5-5"] = new() { InputPerMTok = 2m, OutputPerMTok = 10m },
        ["claude-sonnet-5"] = new() { InputPerMTok = 99m, OutputPerMTok = 99m },   // shorter key – must not win
    };

    [Fact]
    public void Million_input_tokens_at_2_dollars_is_2_million_micro_dollars()
    {
        Assert.Equal(2_000_000, LlmPricing.CostMicroUsd("claude-sonnet-5-5", 1_000_000, 0, Prices));
    }

    [Fact]
    public void Typical_evaluation_costs_about_a_cent_and_a_half()
    {
        // 5 000 in × $2/MTok + 700 out × $10/MTok = $0.010 + $0.007
        Assert.Equal(17_000, LlmPricing.CostMicroUsd("claude-sonnet-5-5", 5_000, 700, Prices));
    }

    [Fact]
    public void Dated_model_snapshot_uses_the_price_of_its_family()
    {
        // The API answers with "claude-haiku-4-5-20251001", the configuration says "claude-haiku-4-5"
        Assert.Equal(2_200 + 310, LlmPricing.CostMicroUsd("claude-haiku-4-5-20251001", 2_200, 62, Prices));
    }

    [Fact]
    public void The_most_specific_price_wins()
    {
        Assert.Equal(2_000, LlmPricing.CostMicroUsd("claude-sonnet-5-5", 1_000, 0, Prices));
    }

    [Theory]
    [InlineData("gpt-5")]
    [InlineData("claude-haiku-4")]          // a prefix of a key is not a match
    [InlineData("claude-haiku-4-50")]       // "-" boundary: 4-50 is not 4-5
    public void Unknown_model_has_no_price(string model)
    {
        Assert.Null(LlmPricing.CostMicroUsd(model, 1000, 1000, Prices));
    }

    [Fact]
    public void Fractions_are_rounded()
    {
        var cheap = new Dictionary<string, ModelPrice> { ["m"] = new() { InputPerMTok = 0.25m, OutputPerMTok = 1.25m } };

        Assert.Equal(2, LlmPricing.CostMicroUsd("m", 1, 1, cheap));   // 0.25 + 1.25 = 1.5 µ$ → rounded away from zero
    }
}
