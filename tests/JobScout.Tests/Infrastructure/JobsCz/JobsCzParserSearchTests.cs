using JobScout.Infrastructure.JobsCz;
using JobScout.Tests.TestUtils;

namespace JobScout.Tests.Infrastructure.JobsCz;

/// <summary>Search results page (/prace/...) → list of JobListing.</summary>
public class JobsCzParserSearchTests
{
    private readonly string _html = Fixture.JobsCz("search-results.html");

    [Fact]
    public void Returns_only_real_job_cards()
    {
        // The page has 5 <article>s: 2 jobs, 1 promo card, 1 banner, 1 card without an ID
        var listings = JobsCzParser.ParseSearchResults(_html);

        Assert.Equal(new[] { "1000000001", "1000000002" }, listings.Select(l => l.ExternalId));
    }

    [Fact]
    public void Maps_all_fields_of_a_card()
    {
        var job = JobsCzParser.ParseSearchResults(_html)[0];

        Assert.Equal("jobs.cz", job.Source);
        Assert.Equal("Senior .NET vývojář (m/ž)", job.Title);
        Assert.Equal("Ukázková firma s.r.o.", job.Company);
        Assert.Equal("Praha – Karlín", job.Location);
        // Canonical URL without tracking parameters (?searchId=…&rps=…)
        Assert.Equal("https://www.jobs.cz/rpd/1000000001/", job.Url);
    }

    [Fact]
    public void Cleans_salary_tag_from_invisible_characters()
    {
        // In HTML: "65&nbsp;000 &zwj;–&zwj; 80&nbsp;000 Kč" – non-breaking spaces and zero-width joiners
        var tags = JobsCzParser.ParseSearchResults(_html)[0].Tags;

        Assert.Equal(new[] { "65 000 – 80 000 Kč", "Možnost občasné práce z domova" }, tags);
        Assert.DoesNotContain(tags, t => t.Contains('\u200D') || t.Contains('\u00A0'));
    }

    [Fact]
    public void Card_without_salary_has_only_other_tags()
    {
        var job = JobsCzParser.ParseSearchResults(_html)[1];

        Assert.Equal("Tech Lead .NET", job.Title);
        Assert.Equal("Druhá ukázková a.s.", job.Company);
        Assert.Equal("Brno", job.Location);
        Assert.Equal(new[] { "Práce převážně z domova" }, job.Tags);
    }

    [Fact]
    public void Page_without_results_returns_empty_list()
    {
        var listings = JobsCzParser.ParseSearchResults("<html><body><p>Nic jsme nenašli.</p></body></html>");

        Assert.Empty(listings);
    }
}
