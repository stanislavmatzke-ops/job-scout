using JobScout.Api.Admin;
using JobScout.Api.Auth;
using JobScout.Api.Matches;
using JobScout.Api.Me;
using JobScout.Api.Profile;
using JobScout.Api.Scouting;
using JobScout.Application;
using JobScout.Infrastructure;
using JobScout.Infrastructure.Persistence;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();
builder.Services.AddFirstSearch(builder.Configuration);   // 6h – the first search right after the first CV
builder.Services.AddJobScoutAuth(builder.Configuration, builder.Environment);
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

var app = builder.Build();

// Create / upgrade the database schema before the first request (same as the Worker)
using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<JobScoutDbContext>().Database.MigrateAsync();

// Bad input (unreadable JSON, wrong type) is the client's fault → 400, not 500.
// In Development ASP.NET Core throws BadHttpRequestException on purpose (to show it in the log) – keep its status code.
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = ex => ex is BadHttpRequestException bad ? bad.StatusCode : StatusCodes.Status500InternalServerError
});

app.UseCsrfHeader();

// The Angular app (6f): built into wwwroot (npm run build), served by the API itself – one origin, so the session
// cookie just works and no CORS is needed. Hashed files (main-AB12CD34.js) never change → cache for a year;
// index.html and the rest must be asked for again, otherwise a new release would not reach the browsers.
app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = ctx => SpaCaching.Apply(ctx.Context, ctx.File.Name) });

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapAuthEndpoints();
app.MapMeEndpoints();
app.MapMatchEndpoints();
app.MapProfileEndpoints();
app.MapAdminEndpoints();
app.MapScoutEndpoints();

// An unknown /api address is an API error (JSON 404), not the app; every other unknown path (/matches/123 after F5)
// gets index.html and the Angular router takes over
app.MapFallback("/api/{**rest}", () => Results.Problem("Neznámá adresa API.", statusCode: StatusCodes.Status404NotFound));
app.MapFallbackToFile("index.html", new StaticFileOptions { OnPrepareResponse = ctx => SpaCaching.Apply(ctx.Context, ctx.File.Name) });

await app.RunAsync();

internal static partial class SpaCaching
{
    public static void Apply(HttpContext context, string fileName) =>
        context.Response.Headers.CacheControl = Hashed().IsMatch(fileName) ? "public, max-age=31536000, immutable" : "no-cache";

    /// <summary>Angular's output hashing: "main-AB12CD34.js", "styles-5INURTSO.css", "chunk-XYZ12345.js".</summary>
    [GeneratedRegex(@"-[A-Z0-9]{8}\.(js|css)$")]
    private static partial Regex Hashed();
}

/// <summary>Visible to the integration tests (WebApplicationFactory&lt;Program&gt;).</summary>
public partial class Program;
