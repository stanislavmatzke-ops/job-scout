using JobScout.Api.Auth;
using JobScout.Api.Me;
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

var app = builder.Build();

// Create / upgrade the database schema before the first request (same as the Worker)
using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<JobScoutDbContext>().Database.MigrateAsync();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapAuthEndpoints();
app.MapMeEndpoints();

await app.RunAsync();

/// <summary>Visible to the integration tests (WebApplicationFactory&lt;Program&gt;).</summary>
public partial class Program;
