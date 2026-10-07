using System.Text.Json;
using System.Text.Json.Serialization;

namespace JobScout.Application.Llm;

/// <summary>Shared JSON settings and parsing of structured LLM responses.</summary>
public static class LlmJson
{
    // Web defaults = camelCase + case-insensitive; enums as camelCase text ("full", "employee", …)
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static T Parse<T>(LlmResponse response)
    {
        // Structured outputs guarantee the schema, not the length – a truncated JSON is invalid
        if (response.StopReason == "max_tokens")
            throw new InvalidOperationException($"Odpověď pro {typeof(T).Name} byla useknuta (max_tokens) – JSON je neúplný.");

        return JsonSerializer.Deserialize<T>(response.Text, Options)
               ?? throw new InvalidOperationException($"Prázdná odpověď pro {typeof(T).Name}.");
    }
    
    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options)
        ?? throw new InvalidOperationException($"Prázdný JSON pro {typeof(T).Name}.");

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}
