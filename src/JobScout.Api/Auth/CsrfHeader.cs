namespace JobScout.Api.Auth;

public static class CsrfHeader
{
    public const string Name = "X-CSRF";

    /// <summary>
    /// State-changing /api calls must carry "X-CSRF: 1" (ADR-0011). A browser can't add a custom header to a
    /// cross-site request without a CORS preflight – and this API never allows cross-site CORS.
    /// </summary>
    public static IApplicationBuilder UseCsrfHeader(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        var request = context.Request;
        var readOnly = HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method);

        if (!readOnly && request.Path.StartsWithSegments("/api") && request.Headers[Name] != "1")
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { title = "Chybí hlavička X-CSRF: 1." });
            return;
        }

        await next(context);
    });
}
