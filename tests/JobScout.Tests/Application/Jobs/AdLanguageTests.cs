using JobScout.Application.Evaluation;
using JobScout.Core;
using JobScout.Application.Jobs;
using JobScout.Application.Llm;
using JobScout.Tests.TestUtils;

namespace JobScout.Tests.Application.Jobs;

/// <summary>
/// The language of the rating and the letter follows the posting – decided in code (12 Oct): an English posting
/// framed by Czech labels and a Czech profile got a Czech rating when the model was left to decide.
/// </summary>
public sealed class AdLanguageTests
{
    private const string EnglishPosting = """
        Pozice: Senior C# Developer
        Firma: Veeam Software (Czech Republic) s.r.o.
        Štítky: Práce z domova, Plný úvazek
        We are looking for a Senior C# Developer to join our team in Prague. You will design and build
        backup features for our flagship product. Your experience with .NET and distributed systems is a must,
        and we offer a friendly team, flexible hours and the chance to work with the latest technologies.
        """;

    private const string CzechPosting = """
        Pozice: Senior C# vývojář
        Hledáme zkušeného vývojáře, který se bude podílet na vývoji našeho systému. Nabízíme práci v přátelském
        týmu, možnost práce z domova a zajímavé projekty. Požadujeme zkušenosti s .NET a znalost SQL. Je pro nás
        důležité, že se rád učíte nové věci. The team is international (English B2).
        """;

    [Fact]
    public void English_posting_with_czech_portal_labels_is_english() =>
        Assert.Equal(AdLanguage.English, AdLanguageDetector.Detect(EnglishPosting));

    [Fact]
    public void Czech_posting_with_a_few_english_words_is_czech() =>
        Assert.Equal(AdLanguage.Czech, AdLanguageDetector.Detect(CzechPosting));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Senior C# Developer")]           // a title alone is not enough to switch away from Czech
    public void Too_little_text_stays_czech(string? text) =>
        Assert.Equal(AdLanguage.Czech, AdLanguageDetector.Detect(text));

    [Fact]
    public async Task The_evaluation_prompt_names_the_output_language()
    {
        var llm = new FakeLlmClient(LlmJson.Serialize(new JobEvaluation(8, "Fits.", [], [], [], "", RemoteWork.Hybrid, EmploymentType.Employee)));

        await new JobEvaluator(llm).EvaluateAsync(TestProfiles.With(["Software developer"]), EnglishPosting);

        Assert.Contains("<jazyk_vystupu>angličtina</jazyk_vystupu>", llm.Requests.Single().UserPrompt);
    }
}
