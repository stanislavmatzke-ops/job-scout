using System.Text;
using JobScout.Application.Evaluation;
using JobScout.Application.Limits;
using JobScout.Application.Llm;
using JobScout.Application.ManualJobs;
using JobScout.Application.Matches;
using JobScout.Core.Domain;
using JobScout.Core.Jobs;
using JobScout.Infrastructure.Web;
using JobScout.Tests.TestUtils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobScout.Tests.Application.ManualJobs;

/// <summary>
/// ADR-0013: a job ad from elsewhere – pasted text, a file, a saved e-mail or a link. The AI reads it into a private ad
/// only its owner sees; a link to a portal we know becomes the normal public ad. Then, if asked, the normal evaluation.
/// </summary>
public sealed class AddJobManuallyTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 15, 6, 0, 0, TimeSpan.Zero);
    private const string AdText = "Hledáme stavbyvedoucího pro rekonstrukce bytových domů v Brně. Mzda 60 000 Kč.";

    private readonly SqliteTestDatabase _database = new();
    private readonly FixedTimeProvider _time = new(T0);
    private readonly FakeJobSource _jobsCz = new("jobs.cz");
    private readonly FakePages _pages = new();
    private readonly FakeLlmClient _llm;
    private readonly Guid _userId;
    private readonly Guid _otherUserId;
    private string _extracted = Extraction();
    private AiLimitOptions _limits = new();

    public AddJobManuallyTests()
    {
        _llm = new FakeLlmClient(request => request.Purpose == LlmPurpose.JobImport
            ? new LlmResponse(_extracted, "fake-model", "end_turn", 100, 50)
            : new LlmResponse(MatchSeeder.EvaluationJson(8, "Sedí obor i místo."), "fake-model", "end_turn", 100, 50));

        using var db = _database.CreateContext();
        var user = new User("dev", "private", "", "private", T0);
        var other = new User("dev", "zednik", "", "zednik", T0);
        db.AddRange(user, other);
        db.SaveChanges();
        (_userId, _otherUserId) = (user.Id, other.Id);
        ProfileSeeder.Add(db, _userId, T0.AddDays(-1));
    }

    public void Dispose() => _database.Dispose();

    // ---------- helpers ----------

    private static string Extraction(bool isJobAd = true, string title = "Stavbyvedoucí", string company = "Stavby Morava a.s.",
        string location = "Brno", string? text = null) =>
        LlmJson.Serialize(new ExtractedJobAd(isJobAd, title, company, location, text));

    private sealed class FakePages : IWebPageReader
    {
        public Func<Uri, WebPage> Respond { get; set; } = url => new WebPage(url, "Stavbyvedoucí – kariéra", AdText, null);
        public List<Uri> Requests { get; } = [];

        public Task<WebPage> ReadAsync(Uri url, CancellationToken ct = default)
        {
            Requests.Add(url);
            return Task.FromResult(Respond(url));
        }
    }

    private async Task<AddJobResult> AddAsync(AddJobInput input, Guid? asUser = null, bool withQuota = false)
    {
        await using var db = _database.CreateContext();
        var evaluator = new MatchEvaluator([_jobsCz], new JobEvaluator(_llm), _time);
        var quota = withQuota
            ? new AiQuota(db, _limits, new AiQuotaGate(), _time, NullLogger<AiQuota>.Instance)
            : null;
        return await new AddJobManually(db, [_jobsCz], new JobAdExtractor(_llm), evaluator, _pages, new MimeEmailReader(),
            _time, NullLogger<AddJobManually>.Instance, quota).ExecuteAsync(asUser ?? _userId, input);
    }

    private JobMatch Stored(Guid matchId)
    {
        using var db = _database.CreateContext();
        return db.JobMatches.Include(m => m.JobOffer).Include(m => m.Events).Single(m => m.Id == matchId);
    }

    private LlmRequest Extract => _llm.Requests.Single(r => r.Purpose == LlmPurpose.JobImport);

    // ---------- pasted text ----------

    [Fact]
    public async Task Pasted_text_becomes_a_private_ad_and_is_evaluated()
    {
        var result = await AddAsync(new AddJobInput(Text: AdText));

        Assert.Equal(AddJobStatus.Added, result.Status);
        Assert.True(result.Evaluated);
        var match = Stored(result.MatchId!.Value);
        Assert.True(match.AddedManually);
        Assert.Equal((MatchStage.Evaluated, 8), (match.Stage, match.Score));
        var offer = match.JobOffer!;
        Assert.Equal((JobOffer.ManualSource, _userId), (offer.Source, offer.OwnerUserId));
        Assert.Equal(("Stavbyvedoucí", "Stavby Morava a.s.", "Brno", ""), (offer.Title, offer.Company, offer.Location, offer.Url));
        Assert.Equal(AdText, offer.DetailText);                     // our own text – the AI only read the facts
        Assert.True(offer.DetailIsComplete);

        Assert.Equal(2, _llm.Calls);
        Assert.Equal(LlmTier.Fast, Extract.Tier);
        Assert.Contains(AdText, Extract.UserPrompt);
        Assert.DoesNotContain("\"text\"", Extract.JsonSchema);     // the text is not asked back – it costs tokens
    }

    [Fact]
    public async Task Without_evaluation_the_job_waits_for_the_button()
    {
        var result = await AddAsync(new AddJobInput(Text: AdText, Evaluate: false));

        Assert.False(result.Evaluated);
        var match = Stored(result.MatchId!.Value);
        Assert.Equal((MatchStage.Shortlisted, (int?)null), (match.Stage, match.Score));
        Assert.Equal(1, _llm.Calls);
        Assert.Equal(MatchView.Open, MatchViews.Of(match.Stage, match.Status));
    }

    [Fact]
    public async Task Initial_status_is_recorded_in_the_history()
    {
        var result = await AddAsync(new AddJobInput(Text: AdText, Status: MatchStatus.Applied, Evaluate: false));

        var match = Stored(result.MatchId!.Value);
        Assert.Equal(MatchStatus.Applied, match.Status);
        var entry = Assert.Single(match.Events);
        Assert.Equal((MatchStatus.New, MatchStatus.Applied), (entry.From, entry.To));
    }

    [Fact]
    public async Task Something_that_is_not_a_job_ad_is_not_saved()
    {
        _extracted = Extraction(isJobAd: false);

        var result = await AddAsync(new AddJobInput(Text: "Faktura za elektřinu, splatnost 20. 10."));

        Assert.Equal(AddJobStatus.NotAJobAd, result.Status);
        Assert.Equal(1, _llm.Calls);                               // no evaluation of nothing
        await using var db = _database.CreateContext();
        Assert.Empty(db.JobOffers);
        Assert.Empty(db.JobMatches);
    }

    [Fact]
    public async Task Long_ai_answers_are_cut_to_the_columns_and_a_missing_title_gets_one()
    {
        _extracted = Extraction(title: "  ", company: new string('F', 400));

        var result = await AddAsync(new AddJobInput(Text: AdText, Evaluate: false));

        var offer = Stored(result.MatchId!.Value).JobOffer!;
        Assert.Equal("Nabídka bez názvu", offer.Title);
        Assert.Equal(300, offer.Company.Length);
    }

    [Fact]
    public async Task When_the_ai_fails_nothing_is_saved_and_the_user_can_retry()
    {
        _extracted = "{ \"isJobAd\": tru";                       // a cut-off, invalid answer

        var result = await AddAsync(new AddJobInput(Text: AdText));

        Assert.Equal(AddJobStatus.AiFailed, result.Status);
        await using var db = _database.CreateContext();
        Assert.Empty(db.JobMatches);
    }

    // ---------- privacy ----------

    [Fact]
    public async Task A_private_ad_is_never_seen_by_somebody_else()
    {
        var result = await AddAsync(new AddJobInput(Text: AdText));

        await using var db = _database.CreateContext();
        var theirs = await new ListMatches(db).ExecuteAsync(_otherUserId, MatchView.Open);
        Assert.Empty(theirs.Items);
        Assert.Null(await new GetMatchDetail(db).ExecuteAsync(_otherUserId, result.MatchId!.Value));
        var mine = await new ListMatches(db).ExecuteAsync(_userId, MatchView.Open);
        Assert.True(Assert.Single(mine.Items).AddedManually);
    }

    [Fact]
    public async Task The_morning_run_leaves_a_job_added_by_hand_alone()
    {
        await AddAsync(new AddJobInput(Text: AdText, Evaluate: false));
        var callsBefore = _llm.Calls;

        await using var db = _database.CreateContext();
        var current = await JobScout.Application.Profiles.ProfileQueries.CurrentProfileAsync(db, _userId);
        var run = await new EvaluateJobs(db, new MatchEvaluator([_jobsCz], new JobEvaluator(_llm), _time))
            .ExecuteAsync(_userId, current!.Profile, current.Version);

        Assert.Empty(run.Evaluated);
        Assert.Equal(callsBefore, _llm.Calls);
    }

    // ---------- files ----------

    [Fact]
    public async Task A_pdf_is_read_by_the_ai_which_also_writes_the_text()
    {
        _extracted = Extraction(text: AdText);
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.7 fake");

        var result = await AddAsync(new AddJobInput(File: new ManualJobFile("inzerat.pdf", pdf), Evaluate: false));

        Assert.Equal(AddJobStatus.Added, result.Status);
        var document = Assert.Single(Extract.Documents);
        Assert.Equal("application/pdf", document.MediaType);
        Assert.Contains("\"text\"", Extract.JsonSchema);
        Assert.Equal(AdText, Stored(result.MatchId!.Value).JobOffer!.DetailText);
    }

    [Fact]
    public async Task A_text_file_is_read_as_text()
    {
        var result = await AddAsync(new AddJobInput(File: new ManualJobFile("inzerat.txt", Encoding.UTF8.GetBytes(AdText)), Evaluate: false));

        Assert.Equal(AdText, Stored(result.MatchId!.Value).JobOffer!.DetailText);
        Assert.Empty(Extract.Documents);
    }

    [Fact]
    public async Task A_saved_email_is_taken_apart_with_its_pdf_attachment()
    {
        var eml = $"""
            From: Petra Nováková <petra@agentura.example>
            To: kandidat@example.com
            Subject: =?UTF-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes("Nabídka: stavbyvedoucí"))}?=
            Date: Tue, 14 Oct 2026 09:30:00 +0200
            MIME-Version: 1.0
            Content-Type: multipart/mixed; boundary="XX"

            --XX
            Content-Type: text/plain; charset=utf-8
            Content-Transfer-Encoding: base64

            {Convert.ToBase64String(Encoding.UTF8.GetBytes("Dobrý den, máme pro Vás pozici stavbyvedoucího v Brně."))}
            --XX
            Content-Type: application/pdf; name="popis.pdf"
            Content-Disposition: attachment; filename="popis.pdf"
            Content-Transfer-Encoding: base64

            {Convert.ToBase64String(Encoding.ASCII.GetBytes("%PDF-1.7 popis pozice"))}
            --XX--
            """;
        _extracted = Extraction(text: AdText);

        var result = await AddAsync(new AddJobInput(File: new ManualJobFile("nabidka.eml", Encoding.UTF8.GetBytes(eml)), Evaluate: false));

        Assert.Equal(AddJobStatus.Added, result.Status);
        Assert.Contains("Předmět: Nabídka: stavbyvedoucí", Extract.UserPrompt);
        Assert.Contains("máme pro Vás pozici stavbyvedoucího", Extract.UserPrompt);
        Assert.Matches("Od: .*Petra Nováková.*<petra@agentura.example>", Extract.UserPrompt);
        var pdf = Assert.Single(Extract.Documents);
        Assert.Equal("%PDF-1.7 popis pozice", Encoding.ASCII.GetString(pdf.Content));
    }

    [Theory]
    [InlineData("nabidka.msg")]
    [InlineData("nabidka.exe")]
    public async Task Unsupported_files_are_refused_before_the_ai(string fileName)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            AddAsync(new AddJobInput(File: new ManualJobFile(fileName, [1, 2, 3]))));

        Assert.Equal(0, _llm.Calls);
    }

    [Fact]
    public async Task A_fake_pdf_is_refused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            AddAsync(new AddJobInput(File: new ManualJobFile("inzerat.pdf", Encoding.UTF8.GetBytes("<html>")))));
    }

    // ---------- links ----------

    [Fact]
    public async Task A_link_to_a_portal_we_know_becomes_the_public_ad_without_ai_reading()
    {
        _jobsCz.Details["123"] = new JobDetail("jobs.cz", "123", "https://jobs.cz/job/123", "Mistr výroby", "Strojírny a.s.", AdText, true);

        var result = await AddAsync(new AddJobInput(Url: "https://jobs.cz/job/123", Evaluate: false));

        Assert.Equal(AddJobStatus.Added, result.Status);
        var match = Stored(result.MatchId!.Value);
        Assert.True(match.AddedManually);
        Assert.Equal(("jobs.cz", "123", (Guid?)null), (match.JobOffer!.Source, match.JobOffer.ExternalId, match.JobOffer.OwnerUserId));
        Assert.Equal(AdText, match.JobOffer.DetailText);
        Assert.Equal(0, _llm.Calls);
        Assert.Empty(_pages.Requests);

        var again = await AddAsync(new AddJobInput(Url: "https://jobs.cz/job/123/"));
        Assert.Equal((AddJobStatus.AlreadyInList, result.MatchId), (again.Status, again.MatchId));
    }

    [Fact]
    public async Task A_known_portal_ad_already_found_for_somebody_else_is_shared_not_copied()
    {
        await using (var db = _database.CreateContext())
            MatchSeeder.Add(db, _otherUserId, "Zedník", T0);
        await using var check = _database.CreateContext();
        var existing = check.JobOffers.Single();
        var url = $"https://jobs.cz/job/{existing.ExternalId}";
        _jobsCz.Details.Clear();                                    // must not be fetched again

        var result = await AddAsync(new AddJobInput(Url: url, Evaluate: false));

        Assert.Equal(existing.Id, Stored(result.MatchId!.Value).JobOfferId);
        Assert.Empty(_jobsCz.DetailRequests);
    }

    [Fact]
    public async Task A_removed_portal_ad_is_reported()
    {
        var result = await AddAsync(new AddJobInput(Url: "https://jobs.cz/job/999"));

        Assert.Equal(AddJobStatus.PageUnavailable, result.Status);
        await using var db = _database.CreateContext();
        Assert.Empty(db.JobMatches);
    }

    [Fact]
    public async Task Any_other_page_is_read_and_its_address_kept()
    {
        var result = await AddAsync(new AddJobInput(Url: "https://kariera.example.cz/pozice/42", Evaluate: false));

        Assert.Equal(new Uri("https://kariera.example.cz/pozice/42"), Assert.Single(_pages.Requests));
        Assert.Contains("Stavbyvedoucí – kariéra", Extract.UserPrompt);
        var offer = Stored(result.MatchId!.Value).JobOffer!;
        Assert.Equal(("https://kariera.example.cz/pozice/42", _userId), (offer.Url, offer.OwnerUserId));
    }

    [Fact]
    public async Task A_page_that_cannot_be_read_says_why_and_calls_no_ai()
    {
        _pages.Respond = _ => throw new WebPageUnavailableException("Stránka neodpověděla včas.");

        var result = await AddAsync(new AddJobInput(Url: "https://kariera.example.cz/pozice/42"));

        Assert.Equal((AddJobStatus.PageUnavailable, "Stránka neodpověděla včas."), (result.Status, result.Message));
        Assert.Equal(0, _llm.Calls);
    }

    [Theory]
    [InlineData("ftp://example.cz/inzerat")]
    [InlineData("kariera.example.cz/pozice")]
    [InlineData("file:///etc/passwd")]
    public async Task Only_web_addresses_are_accepted(string url)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => AddAsync(new AddJobInput(Url: url)));
        Assert.Empty(_pages.Requests);
    }

    // ---------- input and limits ----------

    [Fact]
    public async Task Exactly_one_kind_of_input_is_needed()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => AddAsync(new AddJobInput()));
        await Assert.ThrowsAsync<ArgumentException>(() => AddAsync(new AddJobInput(Text: AdText, Url: "https://example.cz/x")));
        await Assert.ThrowsAsync<ArgumentException>(() => AddAsync(new AddJobInput(Text: AdText, Status: MatchStatus.Hidden)));
    }

    [Fact]
    public async Task Evaluation_without_a_profile_is_refused_before_anything_happens()
    {
        var result = await AddAsync(new AddJobInput(Text: AdText), asUser: _otherUserId);

        Assert.Equal(AddJobStatus.NoProfile, result.Status);
        Assert.Equal(0, _llm.Calls);
    }

    [Fact]
    public async Task Over_the_daily_limit_nothing_is_read_or_saved()
    {
        _limits = new AiLimitOptions { ImportsPer24h = 1 };
        await using (var db = _database.CreateContext())
        {
            db.LlmCalls.Add(LlmCall.Success(_userId, null, nameof(LlmPurpose.JobImport), "fake-model", 100, 50, 1_000, 100, T0.AddHours(-1)));
            await db.SaveChangesAsync();
        }

        var ex = await Assert.ThrowsAsync<AiLimitExceededException>(() =>
            AddAsync(new AddJobInput(Text: AdText), withQuota: true));

        Assert.Equal(AiLimitReason.JobImport, ex.Denial.Reason);
        Assert.Equal(0, _llm.Calls);
        await using var check = _database.CreateContext();
        Assert.Empty(check.JobMatches);
    }

    [Fact]
    public async Task No_evaluations_left_means_the_ad_is_not_even_read()
    {
        _limits = new AiLimitOptions { EvaluationsPer24h = 1 };
        await using (var db = _database.CreateContext())
        {
            db.LlmCalls.Add(LlmCall.Success(_userId, null, nameof(LlmPurpose.Evaluation), "fake-model", 100, 50, 1_000, 100, T0.AddHours(-1)));
            await db.SaveChangesAsync();
        }

        var ex = await Assert.ThrowsAsync<AiLimitExceededException>(() =>
            AddAsync(new AddJobInput(Text: AdText), withQuota: true));

        Assert.Equal(AiLimitReason.Evaluation, ex.Denial.Reason);
        Assert.Equal(0, _llm.Calls);
    }
}
