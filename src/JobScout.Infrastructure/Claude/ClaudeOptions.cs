namespace JobScout.Infrastructure.Claude;

public class ClaudeOptions
{
    public const string SectionName = "Claude";
    public string Model { get; set; } = "claude-haiku-4-5";
    public int MaxTokens { get; set; } = 1024;
}