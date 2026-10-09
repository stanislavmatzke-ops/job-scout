namespace JobScout.Infrastructure.Claude;

internal static class LlmPricing
{
    /// <summary>
    /// Cost in micro-dollars, or null for an unknown model. A dated snapshot ("claude-haiku-4-5-20251001")
    /// uses the price of its family ("claude-haiku-4-5"); the most specific key wins.
    /// </summary>
    public static long? CostMicroUsd(string model, long inputTokens, long outputTokens, IReadOnlyDictionary<string, ModelPrice> prices)
    {
        var price = prices
            .Where(p => model.Equals(p.Key, StringComparison.OrdinalIgnoreCase)
                        || model.StartsWith(p.Key + "-", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.Key.Length)
            .Select(p => p.Value)
            .FirstOrDefault();

        // $/MTok × tokens = micro-dollars (the 10^6 cancels out)
        return price is null
            ? null
            : (long)Math.Round(inputTokens * price.InputPerMTok + outputTokens * price.OutputPerMTok, MidpointRounding.AwayFromZero);
    }
}