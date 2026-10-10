using JobScout.Application.Evaluation;
using JobScout.Application.Jobs;
using JobScout.Application.Letters;
using JobScout.Application.Llm;
using JobScout.Application.Profiles;
using JobScout.Application.Reports;
using JobScout.Application.Scouting;
using JobScout.Application.Triage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JobScout.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<ProfileExtractor>();
        services.AddSingleton<JobEvaluator>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<AnalyzeProfile>();
        services.AddScoped<CategoryResolver>();
        services.AddSingleton<CategorySuggester>();
        services.AddScoped<PlanSearch>();
        services.AddScoped<RecordFoundJobs>();
        services.AddScoped<ScoutRunJournal>();
        services.AddScoped<TriageJobs>();
        services.AddScoped<EvaluateJobs>();
        services.AddScoped<WriteCoverLetters>();
        services.AddScoped<UsageReport>();
        services.AddScoped<BuildMorningReport>();
        services.AddScoped<RunScout>();
        return services;
    }
}
