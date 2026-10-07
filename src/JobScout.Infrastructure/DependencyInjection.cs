using Anthropic;
using JobScout.Application.Abstractions;
using JobScout.Application.Jobs;
using JobScout.Core.Jobs;
using JobScout.Application.Llm;
using JobScout.Infrastructure.Browser;
using JobScout.Infrastructure.Claude;
using JobScout.Infrastructure.JobsCz;
using JobScout.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
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
        services.AddSingleton<IPageRenderer, PlaywrightPageRenderer>();
        services.AddDbContext<JobScoutDbContext>(o => o.UseSqlite(
            config.GetConnectionString("JobScout")
            ?? throw new InvalidOperationException("Chybí ConnectionStrings:JobScout v konfiguraci.")));
        // Use cases ask for IAppDbContext; it's the same scoped instance as JobScoutDbContext
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<JobScoutDbContext>());
        
        return services;
    }
}