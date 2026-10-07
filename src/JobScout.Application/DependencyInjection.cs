using JobScout.Application.Evaluation;
using JobScout.Application.Profiles;
using Microsoft.Extensions.DependencyInjection;

namespace JobScout.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<ProfileExtractor>();
        services.AddSingleton<JobEvaluator>();
        return services;
    }
}