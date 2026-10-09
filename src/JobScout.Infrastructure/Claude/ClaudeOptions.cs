namespace JobScout.Infrastructure.Claude;

public class ClaudeOptions
{
    public const string SectionName = "Claude";
    public ClaudeModels Models { get; set; } = new();
    public int MaxTokens { get; set; } = 1024;
    
    /// <summary>USD per million tokens, by model family ("claude-sonnet-5-5" also prices "claude-sonnet-5-5-2026…").</summary>
    public Dictionary<string, ModelPrice> Pricing { get; set; } = new();
}

public sealed class ModelPrice
{
    public decimal InputPerMTok { get; set; }
    public decimal OutputPerMTok { get; set; }
}

public sealed class ClaudeModels
{
    public string Fast { get; set; } = "claude-haiku-4-5";
    public string Smart { get; set; } = "claude-sonnet-5-5";
}