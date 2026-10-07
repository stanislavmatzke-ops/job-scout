using System.Text;
using JobScout.Core.Domain;

namespace JobScout.Tests.Core.Domain;

/// <summary>Analysed CV version and the "analyse only when the input changes" fingerprint.</summary>
public class UserProfileTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 6, 8, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Cv = Encoding.UTF8.GetBytes("%PDF-1.7 fake CV content");
    private const int V = 2;   // analysis (prompt/schema) version

    [Fact]
    public void Same_cv_and_notes_give_same_hash()
    {
        var a = UserProfile.ComputeInputHash(Cv, "Priorita je home office.", V);
        var b = UserProfile.ComputeInputHash([.. Cv], "Priorita je home office.", V);   // different array, same bytes

        Assert.Equal(a, b);
    }

    [Fact]
    public void Different_notes_give_different_hash()
    {
        var a = UserProfile.ComputeInputHash(Cv, "Priorita je home office.", V);
        var b = UserProfile.ComputeInputHash(Cv, "Priorita je home office. Hledat i Java.", V);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Different_cv_gives_different_hash()
    {
        var a = UserProfile.ComputeInputHash(Cv, null, V);
        var b = UserProfile.ComputeInputHash([.. Cv, (byte)'!'], null, V);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void New_analysis_version_gives_different_hash_for_the_same_input()
    {
        // Prompt or schema changed → every stored profile must be re-analysed on the next run
        var a = UserProfile.ComputeInputHash(Cv, "Priorita je home office.", analysisVersion: 2);
        var b = UserProfile.ComputeInputHash(Cv, "Priorita je home office.", analysisVersion: 3);

        Assert.NotEqual(a, b);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "   ")]
    [InlineData("Priorita HO", "  Priorita HO \n")]   // surrounding whitespace is not a real change
    public void Notes_equivalent_after_trimming_give_same_hash(string? notesA, string? notesB)
    {
        Assert.Equal(UserProfile.ComputeInputHash(Cv, notesA, V), UserProfile.ComputeInputHash(Cv, notesB, V));
    }

    [Fact]
    public void Hash_is_sha256_hex()
    {
        var hash = UserProfile.ComputeInputHash(Cv, "x", V);

        Assert.Matches("^[0-9A-F]{64}$", hash);   // 32 bytes = 64 hex characters
    }

    [Fact]
    public void New_profile_version_keeps_input_and_has_no_search_plan_yet()
    {
        var userId = Guid.NewGuid();
        var hash = UserProfile.ComputeInputHash(Cv, "poznámky", V);

        var profile = new UserProfile(userId, 2, "cv.pdf", "poznámky", hash, """{"headline":"…"}""", "claude-sonnet-5-5", T0);

        Assert.Equal(userId, profile.UserId);
        Assert.Equal(2, profile.Version);
        Assert.Equal(hash, profile.InputHash);
        Assert.Equal("claude-sonnet-5-5", profile.Model);
        Assert.Null(profile.SearchPlanJson);

        profile.SetSearchPlan("""{"categories":["sw-architekt"]}""");

        Assert.Equal("""{"categories":["sw-architekt"]}""", profile.SearchPlanJson);
    }
}
