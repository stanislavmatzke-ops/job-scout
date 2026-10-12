using System.Text.RegularExpressions;

namespace JobScout.Application.Jobs;

public enum AdLanguage { Czech, English }

/// <summary>
/// The language of a job posting, decided in code – not left to the model. The text the model sees is framed in Czech
/// (our labels, Czech tags from the portal, a Czech profile), so "answer in the language of the ad" alone wasn't
/// reliable: an English Veeam posting got a Czech rating. Function words are a robust signal: an English posting
/// is full of "the/and/you", a Czech one of "je/se/na/pro" – the portal's few Czech labels don't change that.
/// </summary>
public static partial class AdLanguageDetector
{
    public static AdLanguage Detect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return AdLanguage.Czech;

        var english = EnglishWords().Matches(text).Count;
        var czech = CzechWords().Matches(text).Count;
        return english >= 8 && english > 3 * czech ? AdLanguage.English : AdLanguage.Czech;
    }

    /// <summary>For prompts (Czech-written): "angličtina" / "čeština".</summary>
    public static string PromptName(this AdLanguage language) => language == AdLanguage.English ? "angličtina" : "čeština";

    [GeneratedRegex(@"\b(the|and|with|you|your|we|our|are|will|for|of|to|in|is|team|experience|skills)\b", RegexOptions.IgnoreCase)]
    private static partial Regex EnglishWords();

    [GeneratedRegex(@"\b(je|jsou|jsme|se|na|pro|že|nebo|také|který|která|které|při|podle|vás|vám|naše|náš|zkušenost\w*|práce|práci|nabízíme|požadujeme)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CzechWords();
}
