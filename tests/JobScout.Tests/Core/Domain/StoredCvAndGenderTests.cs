using JobScout.Core.Domain;

namespace JobScout.Tests.Core.Domain;

/// <summary>
/// The user's current CV is kept (personal data, deleted with the user) so the profile can be re-analysed
/// after new notes or a prompt change without asking for the file again. Grammatical gender is the user's choice.
/// </summary>
public class StoredCvAndGenderTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 10, 8, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Cv = "# Jana Ukázková\nSenior .NET"u8.ToArray();

    [Fact]
    public void New_cv_belongs_to_the_user()
    {
        var userId = Guid.NewGuid();

        var cv = new StoredCv(userId, "cv.md", "text/plain", Cv, T0);

        Assert.Equal((userId, "cv.md", "text/plain", T0), (cv.UserId, cv.FileName, cv.MediaType, cv.UploadedAt));
        Assert.Equal(Cv, cv.Content);
    }

    [Fact]
    public void Upload_replaces_the_previous_cv()
    {
        var cv = new StoredCv(Guid.NewGuid(), "cv.md", "text/plain", Cv, T0);
        byte[] pdf = [.. "%PDF-1.7"u8];

        cv.Replace("zivotopis.pdf", "application/pdf", pdf, T0.AddDays(1));

        Assert.Equal(("zivotopis.pdf", "application/pdf", T0.AddDays(1)), (cv.FileName, cv.MediaType, cv.UploadedAt));
        Assert.Equal(pdf, cv.Content);
    }

    [Fact]
    public void Empty_cv_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new StoredCv(Guid.NewGuid(), "cv.md", "text/plain", [], T0));
    }

    [Fact]
    public void Too_big_cv_is_rejected()
    {
        var huge = new byte[StoredCv.MaxBytes + 1];

        Assert.Throws<ArgumentException>(() => new StoredCv(Guid.NewGuid(), "cv.pdf", "application/pdf", huge, T0));
    }

    [Fact]
    public void New_user_has_no_gender_until_they_choose()
    {
        var user = new User("google", "g-1", "jana@example.com", "Jana", T0);

        Assert.Equal(GrammaticalGender.Unspecified, user.Gender);

        user.SetGender(GrammaticalGender.Feminine);
        Assert.Equal(GrammaticalGender.Feminine, user.Gender);
    }

    [Fact]
    public void Undefined_gender_value_is_rejected()
    {
        var user = new User("google", "g-1", "", "Jana", T0);

        Assert.Throws<ArgumentOutOfRangeException>(() => user.SetGender((GrammaticalGender)42));
    }
}
