using JobScout.Application.Evaluation;
using JobScout.Application.Profiles;
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
        return services;
    }
}