using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace JobScout.Infrastructure.Browser;

/// <summary>Loads a page in a real (headless) browser and returns the HTML after JavaScript has run.</summary>
internal interface IPageRenderer
{
    Task<string> RenderAsync(Uri url, string readySelector, CancellationToken ct = default);
}

/// <summary>
/// One shared Chromium for the whole app run (starting it is the expensive part),
/// a fresh isolated context per page, one page at a time.
/// </summary>
internal sealed class PlaywrightPageRenderer(ILogger<PlaywrightPageRenderer> logger) : IPageRenderer, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public async Task<string> RenderAsync(Uri url, string readySelector, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var browser = await GetBrowserAsync();
            await using var context = await browser.NewContextAsync(new() { Locale = "cs-CZ" });
            var page = await context.NewPageAsync();

            // Fetch only what the text needs – no images, fonts, styles or analytics (faster, and fair to the site's stats)
            await page.RouteAsync("**/*", route =>
                route.Request.ResourceType is "image" or "font" or "media" or "stylesheet" || IsTracker(route.Request.Url)
                    ? route.AbortAsync()
                    : route.ContinueAsync());

            var sw = Stopwatch.StartNew();
            await page.GotoAsync(url.ToString(), new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30_000 });
            await page.WaitForSelectorAsync(readySelector, new() { Timeout = 15_000 });   // data arrived
            var html = await page.ContentAsync();

            logger.LogInformation("Prohlížeč: {Host} vykreslen za {Ms} ms", url.Host, sw.ElapsedMilliseconds);
            return html;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IBrowser> GetBrowserAsync()
    {
        if (_browser is not null)
            return _browser;

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
        logger.LogInformation("Prohlížeč Chromium spuštěn");
        return _browser;
    }

    private static bool IsTracker(string url) =>
        url.Contains("google", StringComparison.OrdinalIgnoreCase)
        || url.Contains("doubleclick", StringComparison.OrdinalIgnoreCase)
        || url.Contains("facebook", StringComparison.OrdinalIgnoreCase)
        || url.Contains("hotjar", StringComparison.OrdinalIgnoreCase);

    // The DI container calls this on app shutdown – the browser process must not be left running
    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
            await _browser.DisposeAsync();
        _playwright?.Dispose();
    }
}