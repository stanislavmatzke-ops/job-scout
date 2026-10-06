using JobScout.Infrastructure.Browser;

namespace JobScout.Tests.TestUtils;

/// <summary>
/// Replaces the real Playwright browser in tests: returns prepared HTML or fails on purpose.
/// Records calls so a test can check whether the browser was used at all.
/// </summary>
internal sealed class FakePageRenderer : IPageRenderer
{
    private readonly string? _html;
    private readonly Exception? _error;

    private FakePageRenderer(string? html, Exception? error) => (_html, _error) = (html, error);

    public static FakePageRenderer Returning(string html) => new(html, null);
    public static FakePageRenderer Failing(Exception error) => new(null, error);

    public List<Uri> Calls { get; } = [];

    public Task<string> RenderAsync(Uri url, string readySelector, CancellationToken ct = default)
    {
        Calls.Add(url);
        return _error is not null
            ? Task.FromException<string>(_error)
            : Task.FromResult(_html!);
    }
}
