using JobScout.Core.Evaluation;
using JobScout.Core.Profile;
using JobScout.Infrastructure;
using JobScout.Infrastructure.Persistence;
using JobScout.Worker;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddSimpleConsole(o => o.TimestampFormat = "HH:mm:ss ");
builder.Services.AddHostedService<Worker>();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton<ProfileExtractor>();
builder.Services.AddSingleton<JobEvaluator>();

var host = builder.Build();

// Create / upgrade the database schema before the worker starts
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<JobScoutDbContext>();
    await db.Database.MigrateAsync();
}

await host.RunAsync();
