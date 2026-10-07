using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using JobScout.Application.Llm;
using Microsoft.Extensions.Options;

namespace JobScout.Infrastructure.Claude;

internal sealed class ClaudeLlmClient(IAnthropicClient client, IOptions<ClaudeOptions> options) : ILlmClient
{
    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        var o = options.Value;

        var message = await client.Messages.Create(new MessageCreateParams
        {
            Model = request.Tier switch
            {
                LlmTier.Fast => o.Models.Fast,
                _ => o.Models.Smart,
            },
            MaxTokens = request.MaxTokens ?? o.MaxTokens,
            System = request.SystemPrompt,
            Messages = [new() { Role = Role.User, Content = BuildContent(request) }],
            OutputConfig = request.JsonSchema is null
                ? null
                : new OutputConfig { Format = new JsonOutputFormat { Schema = ParseSchema(request.JsonSchema) } },
        }, cancellationToken: ct);

        var text = string.Concat(message.Content.Select(b => b.TryPickText(out var t) ? t.Text : ""));

        // Raw() = the value exactly as the API sent it ("end_turn", "claude-haiku-4-5-20251001")
        return new LlmResponse(
            text,
            message.Model.Raw(),
            message.StopReason?.Raw(),
            message.Usage.InputTokens,
            message.Usage.OutputTokens);
    }

    // The user message is a list of content blocks: documents first, then the prompt text.
    // Claude reads a PDF natively – text and page images – so no PDF parsing on our side.
    private static List<ContentBlockParam> BuildContent(LlmRequest request)
    {
        var blocks = new List<ContentBlockParam>();

        foreach (var doc in request.Documents)
        {
            DocumentBlockParamSource source = doc.MediaType switch
            {
                "application/pdf" => new Base64PdfSource { Data = Convert.ToBase64String(doc.Content) },
                "text/plain" => new PlainTextSource { Data = Encoding.UTF8.GetString(doc.Content) },
                _ => throw new NotSupportedException($"Nepodporovaný typ dokumentu: {doc.MediaType}"),
            };

            blocks.Add(new DocumentBlockParam { Source = source, Title = doc.Title });
        }

        blocks.Add(new TextBlockParam { Text = request.UserPrompt });
        return blocks;
    }

    private static Dictionary<string, JsonElement> ParseSchema(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)
        ?? throw new ArgumentException("Neplatné JSON Schema", nameof(json));
}
