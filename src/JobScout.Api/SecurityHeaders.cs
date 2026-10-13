namespace JobScout.Api;

/// <summary>
/// HTTP security headers for every answer (13 Oct, review against common "vibe coding" risks).
/// The Content-Security-Policy is report-only first: the browser only reports what it would block (DevTools console),
/// nothing breaks. Once the console is clean in production, rename the header to enforce it.
/// </summary>
public static class SecurityHeaders
{
    /// <summary>
    /// Our own scripts only; Angular Material sets inline styles; Material Icons come from Google Fonts (index.html).
    /// No framing (clickjacking), no plugins, forms and &lt;base&gt; only to ourselves.
    /// </summary>
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com data:; img-src 'self' data:; connect-src 'self'; " +
        "object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";                                  // a file is what its type says
            headers.XFrameOptions = "DENY";                                           // nobody embeds us in a frame
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";           // no job ids leak to other sites
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            headers["Content-Security-Policy-Report-Only"] = ContentSecurityPolicy;
            await next();
        });
}
