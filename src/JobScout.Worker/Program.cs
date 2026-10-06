using JobScout.Core.Evaluation;
using JobScout.Core.Profile;
using JobScout.Infrastructure;
using JobScout.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddSimpleConsole(o => o.TimestampFormat = "HH:mm:ss ");
builder.Services.AddHostedService<Worker>();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton<ProfileExtractor>();
builder.Services.AddSingleton<JobEvaluator>();

var host = builder.Build();
host.Run();
