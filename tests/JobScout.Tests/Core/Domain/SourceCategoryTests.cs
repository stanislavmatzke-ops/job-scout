using JobScout.Core.Domain;

namespace JobScout.Tests.Core.Domain;

/// <summary>Learned catalogue entry (ADR-0015): what a job source answered for a profession name.</summary>
public class SourceCategoryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 7, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Lakýrník", "lakyrnik")]
    [InlineData("  Zdravotní   sestra ", "zdravotni-sestra")]
    [InlineData("SW architekt", "sw-architekt")]
    [InlineData(".NET vývojář", "net-vyvojar")]
    [InlineData("Řidič sk. C", "ridic-sk-c")]
    [InlineData("LAKÝRNÍK", "lakyrnik")]          // case doesn't matter
    [InlineData("–", "")]
    public void NormalizeKey_is_lowercase_ascii_with_dashes(string label, string expected)
    {
        Assert.Equal(expected, SourceCategory.NormalizeKey(label));
    }

    [Fact]
    public void Existing_category_keeps_source_key_label_and_id()
    {
        var c = new SourceCategory("jobs.cz", " Lakýrník ", "lakyrnik", T0);

        Assert.Equal("jobs.cz", c.Source);
        Assert.Equal("lakyrnik", c.Key);
        Assert.Equal("Lakýrník", c.Label);
        Assert.Equal("lakyrnik", c.CategoryId);
        Assert.True(c.Exists);
        Assert.Equal(T0, c.CheckedAt);
    }

    [Fact]
    public void Negative_answer_is_remembered_too()
    {
        // "autolakyrnik" doesn't exist on jobs.cz – knowing that saves a request next time
        var c = new SourceCategory("jobs.cz", "Autolakýrník", categoryId: null, T0);

        Assert.False(c.Exists);
        Assert.Equal("autolakyrnik", c.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("–")]
    public void Empty_label_is_rejected(string label)
    {
        Assert.Throws<ArgumentException>(() => new SourceCategory("jobs.cz", label, null, T0));
    }

    [Fact]
    public void Staleness_depends_on_age()
    {
        var c = new SourceCategory("jobs.cz", "Zedník", "zednik", T0);

        Assert.False(c.IsStale(T0.AddDays(89), TimeSpan.FromDays(90)));
        Assert.True(c.IsStale(T0.AddDays(91), TimeSpan.FromDays(90)));
    }

    [Fact]
    public void Recheck_updates_answer_and_time()
    {
        var c = new SourceCategory("jobs.cz", "Zedník", "zednik", T0);

        c.Recheck(categoryId: null, T0.AddDays(100));     // the portal removed the profession

        Assert.False(c.Exists);
        Assert.Equal(T0.AddDays(100), c.CheckedAt);
        Assert.Equal("zednik", c.Key);                    // identity stays
    }
}
