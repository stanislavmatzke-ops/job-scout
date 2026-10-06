using Anthropic;
using JobScout.Core.Llm;
using JobScout.Core.Evaluation;
using JobScout.Core.Jobs;
using JobScout.Infrastructure.Claude;
using JobScout.Infrastructure.JobsCz;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JobScout.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<ClaudeOptions>()
            .Bind(config.GetSection(ClaudeOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Models.Fast) && !string.IsNullOrWhiteSpace(o.Models.Smart),
                "Claude:Models:Fast a Claude:Models:Smart musí být vyplněné.")
            .ValidateOnStart();
        services.AddSingleton<IAnthropicClient>(_ => new AnthropicClient()); // klíč z ANTHROPIC_API_KEY
        services.AddSingleton<ILlmClient, ClaudeLlmClient>();
        services.AddHttpClient<IJobSource, JobsCzSource>(c =>
        {
            c.BaseAddress = new Uri("https://www.jobs.cz");
            c.DefaultRequestHeaders.UserAgent.ParseAdd(
                "JobScout/0.1 (personal job search assistant; learning project)");
            c.DefaultRequestHeaders.AcceptLanguage.ParseAdd("cs-CZ");
            c.Timeout = TimeSpan.FromSeconds(30);
        }).AddHttpMessageHandler(() => new PoliteDelayHandler(TimeSpan.FromSeconds(2)));
        return services;
    }
}