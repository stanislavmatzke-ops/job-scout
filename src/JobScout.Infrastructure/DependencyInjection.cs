using Anthropic;
using JobScout.Application.Abstractions;
using JobScout.Application.Jobs;
using JobScout.Core.Jobs;
using JobScout.Application.Limits;
using JobScout.Application.Llm;
using Azure.Communication.Email;
using JobScout.Application.Notifications;
using JobScout.Application.ManualJobs;
using JobScout.Infrastructure.Browser;
using JobScout.Infrastructure.Web;
using JobScout.Infrastructure.Email;
using JobScout.Infrastructure.Claude;
using JobScout.Infrastructure.JobsCz;
using JobScout.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
        services.AddHttpClient<IJobSource, JobsCzSource>(c =>
        {
            c.BaseAddress = new Uri("https://www.jobs.cz");
            c.DefaultRequestHeaders.UserAgent.ParseAdd(
                "JobScout/0.1 (personal job search assistant; learning project)");
            c.DefaultRequestHeaders.AcceptLanguage.ParseAdd("cs-CZ");
            c.Timeout = TimeSpan.FromMinutes(2);      // covers the ClassicSiteHandler's pauses (under a minute) too
        })
        // No cookies: jobs.cz picks the classic site or the beta per visitor (cookie beta_bucket) – without one, every
        // request gets its own pick instead of a whole run sticking to the beta (ClassicSiteHandler)
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = true })
        // Outer first: a retry from the beta still waits its turn in the PoliteDelayHandler
        .AddHttpMessageHandler(sp => new ClassicSiteHandler(sp.GetRequiredService<ILogger<ClassicSiteHandler>>()))
        .AddHttpMessageHandler(() => new PoliteDelayHandler(TimeSpan.FromSeconds(2)));
        services.AddSingleton<IPageRenderer, PlaywrightPageRenderer>();
        // Job ads the user adds by hand (ADR-0013): any public page they paste a link to – never inside our network
        services.AddHttpClient<IWebPageReader, WebPageReader>(c =>
        {
            c.DefaultRequestHeaders.UserAgent.ParseAdd("JobScout/0.1 (personal job search assistant; reads a page the user asked for)");
            c.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/pdf;q=0.9,text/plain;q=0.8");
            c.DefaultRequestHeaders.AcceptLanguage.ParseAdd("cs-CZ,cs;q=0.9,en;q=0.8");
            c.Timeout = TimeSpan.FromSeconds(20);
        }).ConfigurePrimaryHttpMessageHandler(WebPageReader.SafeHandler);
        services.AddSingleton<IEmailReader, MimeEmailReader>();
        // Factory for short-lived contexts (usage recorder) + the usual scoped context for use cases
        // SQLite locally and in tests, PostgreSQL in Azure – Database:Provider (ADR-0018)
        services.AddDbContextFactory<JobScoutDbContext>(o => DatabaseProvider.Configure(o, config));
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<JobScoutDbContext>>().CreateDbContext());
        // Use cases ask for IAppDbContext; it's the same scoped instance as JobScoutDbContext
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<JobScoutDbContext>());
        // AI client = the real Claude client wrapped in the usage recorder (decorator, ADR-0016)
        services.AddSingleton<ClaudeLlmClient>();
        services.AddSingleton<ILlmClient>(sp =>
            ActivatorUtilities.CreateInstance<UsageRecordingLlmClient>(sp, sp.GetRequiredService<ClaudeLlmClient>()));

        // E-mails (7f): Azure Communication Services when configured, otherwise only a log line
        services.AddSingleton(config.GetSection(NotificationOptions.SectionName).Get<NotificationOptions>() ?? new NotificationOptions());
        var emailConnection = config["Email:ConnectionString"];
        var emailFrom = config["Email:From"];
        if (!string.IsNullOrWhiteSpace(emailConnection) && !string.IsNullOrWhiteSpace(emailFrom))
            services.AddSingleton<IEmailSender>(sp => new AcsEmailSender(
                new EmailClient(emailConnection), emailFrom, sp.GetRequiredService<ILogger<AcsEmailSender>>()));
        else
            services.AddSingleton<IEmailSender, LogEmailSender>();
        services.AddScoped<Notifier>();

        // AI limits for clicks (ADR-0021) – defaults in AiLimitOptions, overridable per environment
        services.AddSingleton(config.GetSection(AiLimitOptions.SectionName).Get<AiLimitOptions>() ?? new AiLimitOptions());

        return services;
    }
}