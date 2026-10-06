namespace JobScout.Infrastructure.Claude;

public class ClaudeOptions
{
    public const string SectionName = "Claude";
    public ClaudeModels Models { get; set; } = new();
    public int MaxTokens { get; set; } = 1024;
}

public sealed class ClaudeModels
{
    public string Fast { get; set; } = "claude-haiku-4-5";
    public string Smart { get; set; } = "claude-sonnet-5-5";
}