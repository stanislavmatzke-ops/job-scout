using JobScout.Application.Evaluation;
using JobScout.Application.Jobs;
using JobScout.Application.Letters;
using JobScout.Application.Profiles;
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
        return services;
    }
}