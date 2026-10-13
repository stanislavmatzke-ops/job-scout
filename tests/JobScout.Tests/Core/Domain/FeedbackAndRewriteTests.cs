using JobScout.Core.Domain;

namespace JobScout.Tests.Core.Domain;

/// <summary>"Zpětná vazba" is validated in the entity; a new letter over an existing one is counted (ADR-0021).</summary>
public class FeedbackAndRewriteTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 13, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Feedback_keeps_what_the_user_wrote_trimmed()
    {
        var userId = Guid.NewGuid();

        var feedback = new Feedback(userId, "Jana", FeedbackKind.Idea, rating: 4, "  Přidejte prace.cz  ", replyWanted: true, T0);

        Assert.NotEqual(Guid.Empty, feedback.Id);
        Assert.Equal((userId, "Jana", FeedbackKind.Idea, (int?)4, "Přidejte prace.cz", true, T0),
            (feedback.UserId, feedback.UserName, feedback.Kind, feedback.Rating, feedback.Message, feedback.ReplyWanted, feedback.CreatedAt));
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("Ok", 0)]
    [InlineData("Ok", 6)]
    public void Feedback_without_text_or_with_a_wrong_rating_is_refused(string message, int? rating)
    {
        Assert.Throws<ArgumentException>(() =>
            new Feedback(Guid.NewGuid(), "Jana", FeedbackKind.Other, rating, message, false, T0));
    }

    [Fact]
    public void Too_long_feedback_is_refused()
    {
        Assert.Throws<ArgumentException>(() =>
            new Feedback(Guid.NewGuid(), "Jana", FeedbackKind.Other, null, new string('x', Feedback.MaxMessageLength + 1), false, T0));
    }

    [Fact]
    public void Rewriting_a_letter_is_counted_writing_the_first_one_is_not()
    {
        var match = new JobMatch(Guid.NewGuid(), Guid.NewGuid(), profileVersion: 1, T0);

        match.SetCoverLetter("První návrh", T0);
        Assert.Equal(0, match.LetterRewrites);

        match.RewriteCoverLetter("Druhý návrh", T0.AddMinutes(1));
        match.RewriteCoverLetter("Třetí návrh", T0.AddMinutes(2));

        Assert.Equal(("Třetí návrh", 2, T0.AddMinutes(2)), (match.CoverLetter, match.LetterRewrites, match.UpdatedAt));
    }

    [Fact]
    public void There_is_nothing_to_rewrite_without_a_letter()
    {
        var match = new JobMatch(Guid.NewGuid(), Guid.NewGuid(), profileVersion: 1, T0);

        Assert.Throws<InvalidOperationException>(() => match.RewriteCoverLetter("Návrh", T0));
    }
}
