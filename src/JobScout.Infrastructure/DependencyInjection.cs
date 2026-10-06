using Anthropic;
using JobScout.Core.Llm;
using JobScout.Infrastructure.Claude;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JobScout.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<ClaudeOptions>().Bind(config.GetSection(ClaudeOptions.SectionName));
        services.AddSingleton<IAnthropicClient>(_ => new AnthropicClient()); // klíč z ANTHROPIC_API_KEY
        services.AddSingleton<ILlmClient, ClaudeLlmClient>();
        return services;
    }
}