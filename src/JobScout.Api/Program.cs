using JobScout.Api.Admin;
using JobScout.Api.Auth;
using JobScout.Api.Matches;
using JobScout.Api.Me;
using JobScout.Api.Profile;
using JobScout.Application;
using JobScout.Infrastructure;
using JobScout.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();
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

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapAuthEndpoints();
app.MapMeEndpoints();
app.MapMatchEndpoints();
app.MapProfileEndpoints();
app.MapAdminEndpoints();

await app.RunAsync();

/// <summary>Visible to the integration tests (WebApplicationFactory&lt;Program&gt;).</summary>
public partial class Program;
