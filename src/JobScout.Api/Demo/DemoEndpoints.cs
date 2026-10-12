using JobScout.Api.Auth;
using JobScout.Application.Demo;
using JobScout.Application.Users;
using Microsoft.AspNetCore.Authentication;

namespace JobScout.Api.Demo;

/// <summary>Demo:Enabled – the "Try it without signing up" button (ADR-0020). Works in Production, unlike dev sign-in.</summary>
public sealed class DemoOptions
{
    public const string SectionName = "Demo";
    public bool Enabled { get; init; }
}

public static class DemoEndpoints
{
    public static IServiceCollection AddDemo(this IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton(config.GetSection(DemoOptions.SectionName).Get<DemoOptions>() ?? new DemoOptions());
        services.AddScoped<PrepareDemo>();
        return services;
    }

    public static IEndpointRouteBuilder MapDemoEndpoints(this IEndpointRouteBuilder app)
    {
        // Switched off = the endpoints don't exist (404), like dev sign-in
        if (!app.ServiceProvider.GetRequiredService<DemoOptions>().Enabled)
            return app;

        // Everyone who clicks the button is the same fictional user – no account, no password, no personal data
        app.MapPost("/api/auth/demo", async (HttpContext http, SignInUser signIn, CancellationToken ct) =>
        {
            var result = await signIn.ExecuteAsync(
                new SignInRequest(DemoAccount.Provider, DemoAccount.Subject, Email: "", DemoAccount.DisplayName), ct);
            await http.SignInAsync(AuthEndpoints.CreatePrincipal(result.User!));
            return Results.NoContent();
        });

        app.MapPost("/api/admin/demo/prepare", async (PrepareDemo prepare, CancellationToken ct) =>
                Results.Ok(await prepare.ExecuteAsync(ct)))
            .RequireAuthorization(Policies.Admin);

        return app;
    }
}
