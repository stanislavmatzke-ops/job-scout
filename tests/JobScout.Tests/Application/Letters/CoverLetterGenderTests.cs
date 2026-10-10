using JobScout.Application.Letters;
using JobScout.Application.Llm;
using JobScout.Application.Matches;
using JobScout.Core.Domain;
using JobScout.Tests.TestUtils;

namespace JobScout.Tests.Application.Letters;

/// <summary>
/// Czech needs the grammatical gender ("rád" / "ráda"). The user chooses it – it is never guessed from the name.
/// Unspecified = the model must avoid gendered forms.
/// </summary>
public sealed class CoverLetterGenderTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 10, 6, 0, 0, TimeSpan.Zero);

    private readonly SqliteTestDatabase _database = new();
    private readonly FixedTimeProvider _time = new(T0);
    private readonly FakeLlmClient _llm = new("Dobrý den,\n\n…\n\nS pozdravem\n[Jméno a příjmení]");

    public void Dispose() => _database.Dispose();

    private async Task<LlmRequest> LetterRequestFor(GrammaticalGender gender)
    {
        Guid userId, matchId;
        await using (var db = _database.CreateContext())
        {
            var user = new User("dev", $"persona-{gender}", "", "Persona", T0);
            user.SetGender(gender);
            db.Users.Add(user);
            db.SaveChanges();
            userId = user.Id;
            ProfileSeeder.Add(db, userId, T0);
            matchId = MatchSeeder.Add(db, userId, "Architekt", T0, MatchStage.Evaluated, score: 8, adText: "Hledáme architekta.");
        }

        await using (var db = _database.CreateContext())
            await new WriteCoverLetterNow(db, new CoverLetterWriter(_llm), _time).ExecuteAsync(userId, matchId);

        return _llm.Requests.Last();
    }

    [Theory]
    [InlineData(GrammaticalGender.Masculine, "<rod>mužský</rod>")]
    [InlineData(GrammaticalGender.Feminine, "<rod>ženský</rod>")]
    [InlineData(GrammaticalGender.Unspecified, "<rod>neuvedený</rod>")]
    public async Task Letter_prompt_carries_the_chosen_gender(GrammaticalGender gender, string expected)
    {
        var request = await LetterRequestFor(gender);

        Assert.Contains(expected, request.UserPrompt);
    }

    [Fact]
    public async Task Morning_run_letters_use_the_gender_too()
    {
        Guid userId;
        await using (var db = _database.CreateContext())
        {
            var user = new User("dev", "jana", "", "Jana", T0);
            user.SetGender(GrammaticalGender.Feminine);
            db.Users.Add(user);
            db.SaveChanges();
            userId = user.Id;
            MatchSeeder.Add(db, userId, "Architekt", T0, MatchStage.Evaluated, score: 8, adText: "Hledáme architekta.");
        }

        await using (var db = _database.CreateContext())
            await new WriteCoverLetters(db, new CoverLetterWriter(_llm), _time)
                .ExecuteAsync(userId, TestProfiles.With(["Softwarový architekt"]));

        Assert.Contains("<rod>ženský</rod>", Assert.Single(_llm.Requests).UserPrompt);
    }
}
