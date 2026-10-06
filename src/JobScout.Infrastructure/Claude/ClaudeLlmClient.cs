using Anthropic;
using Anthropic.Models.Messages;
using JobScout.Core.Llm;
using Microsoft.Extensions.Options;

namespace JobScout.Infrastructure.Claude;

internal sealed class ClaudeLlmClient(IAnthropicClient client, IOptions<ClaudeOptions> options) : ILlmClient
{
    public async Task<LlmResponse> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var o = options.Value;

        var message = await client.Messages.Create(new MessageCreateParams
        {
            Model = o.Model,
            MaxTokens = o.MaxTokens,
            System = systemPrompt,
            Messages = [new() { Role = Role.User, Content = userPrompt }],
        }, cancellationToken: ct);
        
        var text = string.Concat(message.Content.Select(b => b.TryPickText(out var t) ? t.Text : ""));

        return new LlmResponse(
            text,
            message.Model.ToString().Trim('"'),
            message.StopReason?.ToString().Trim('"'),
            message.Usage.InputTokens,
            message.Usage.OutputTokens);
    }
}