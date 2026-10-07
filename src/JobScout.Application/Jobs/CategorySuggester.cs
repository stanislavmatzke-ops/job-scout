using JobScout.Application.Llm;

namespace JobScout.Application.Jobs;

public sealed record ProfessionMapping(string Profession, IReadOnlyList<string> Labels);
public sealed record CategorySuggestions(IReadOnlyList<ProfessionMapping> Mappings);
public sealed record CategorySuggestionResult(CategorySuggestions Suggestions, LlmResponse Response);

/// <summary>Candidate's professions → catalogue-style names to verify. Simple task → the cheap Fast model.</summary>
public sealed class CategorySuggester(ILlmClient llm)
{
    public async Task<CategorySuggestionResult> SuggestAsync(
        string sourceName, IReadOnlyList<string> professions, IReadOnlyList<JobSearch.JobCategory> known,
        CancellationToken ct = default)
    {
        var knownText = known.Count == 0 ? "(zatím žádné)" : string.Join("\n", known.Select(c => c.Label));

        var request = new LlmRequest(
            CategoryPrompts.System,
            $"""
             Portál: {sourceName}

             <overene_profese>
             {knownText}
             </overene_profese>

             <profese_kandidata>
             {string.Join("\n", professions)}
             </profese_kandidata>
             """)
        {
            JsonSchema = CategoryPrompts.Schema,
            MaxTokens = 800,
            Tier = LlmTier.Smart,
        };

        var response = await llm.CompleteAsync(request, ct);
        return new CategorySuggestionResult(LlmJson.Parse<CategorySuggestions>(response), response);
    }
}