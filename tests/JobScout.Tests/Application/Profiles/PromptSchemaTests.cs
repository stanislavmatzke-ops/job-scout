using System.Reflection;
using System.Text.Json;
using JobScout.Application.Evaluation;
using JobScout.Application.Llm;
using JobScout.Application.Profiles;

namespace JobScout.Tests.Application.Profiles;

/// <summary>
/// The JSON Schema sent to the model and the C# record that parses the answer are written by hand in two places.
/// These tests catch drift between them – e.g. a property added to the record but forgotten in the schema
/// (the model would never fill it) or left in the schema after removing it from the record (wasted tokens).
/// </summary>
public class PromptSchemaTests
{
    public static TheoryData<string, Type> Schemas => new()
    {
        { ProfilePrompts.Schema, typeof(CandidateProfile) },
        { EvaluationPrompts.Schema, typeof(JobEvaluation) },
    };

    [Theory]
    [MemberData(nameof(Schemas))]
    public void Schema_is_valid_json_with_closed_objects(string schema, Type _)
    {
        using var doc = JsonDocument.Parse(schema);

        foreach (var obj in Objects(doc.RootElement, "$"))
        {
            // Structured outputs need additionalProperties: false and every property required
            Assert.True(obj.Element.TryGetProperty("additionalProperties", out var ap) && ap.ValueKind == JsonValueKind.False,
                $"{obj.Path}: missing \"additionalProperties\": false");

            var properties = obj.Element.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToHashSet();
            var required = obj.Element.GetProperty("required").EnumerateArray().Select(r => r.GetString()!).ToHashSet();
            Assert.True(properties.SetEquals(required),
                $"{obj.Path}: required [{string.Join(", ", required)}] ≠ properties [{string.Join(", ", properties)}]");
        }
    }

    [Theory]
    [MemberData(nameof(Schemas))]
    public void Schema_properties_match_the_record(string schema, Type record)
    {
        using var doc = JsonDocument.Parse(schema);
        AssertSameShape(doc.RootElement, record, record.Name);
    }

    [Fact]
    public void Analysis_version_is_set()
    {
        // Bump ProfilePrompts.Version whenever ProfilePrompts change → stored profiles get re-analysed
        Assert.True(ProfilePrompts.Version >= 2);
    }

    [Fact]
    public void Profile_prompt_is_not_it_specific()
    {
        // ADR-0014: the app serves car painters as well as architects
        Assert.DoesNotContain("IT recruiter", ProfilePrompts.System);
        Assert.DoesNotContain("IT recruiter", EvaluationPrompts.System);
        Assert.DoesNotContain("v IT", ProfilePrompts.Schema);
    }

    // ---------- helpers ----------

    private static void AssertSameShape(JsonElement schemaObject, Type record, string path)
    {
        var schemaProps = schemaObject.GetProperty("properties");
        var schemaNames = schemaProps.EnumerateObject().Select(p => p.Name).ToHashSet();

        var recordProps = record.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetMethod is { IsPublic: true } && p.Name != "EqualityContract")
            .ToDictionary(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name));

        var missingInSchema = recordProps.Keys.Except(schemaNames).ToList();
        var missingInRecord = schemaNames.Except(recordProps.Keys).ToList();

        Assert.True(missingInSchema.Count == 0, $"{path}: in record but not in schema: {string.Join(", ", missingInSchema)}");
        Assert.True(missingInRecord.Count == 0, $"{path}: in schema but not in record: {string.Join(", ", missingInRecord)}");

        // Nested objects (preferences, search) must match their records too
        foreach (var (name, property) in recordProps)
        {
            var element = schemaProps.GetProperty(name);
            if (element.TryGetProperty("type", out var t) && t.GetString() == "object")
                AssertSameShape(element, property.PropertyType, $"{path}.{name}");
        }
    }

    private static IEnumerable<(string Path, JsonElement Element)> Objects(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Object) yield break;

        if (element.TryGetProperty("type", out var t) && t.GetString() == "object")
            yield return (path, element);

        foreach (var child in element.EnumerateObject())
            foreach (var nested in Objects(child.Value, $"{path}.{child.Name}"))
                yield return nested;
    }
}
