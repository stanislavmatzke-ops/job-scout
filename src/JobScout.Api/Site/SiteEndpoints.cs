namespace JobScout.Api.Site;

/// <summary>
/// Operator: who runs this instance – the data controller named on the privacy page (GDPR). Configuration, not code:
/// a fork of the repository runs under its own name and contact.
/// </summary>
public sealed class OperatorOptions
{
    public const string SectionName = "Operator";

    public string Name { get; init; } = "";
    public string Email { get; init; } = "";

    /// <summary>"About the project" link in the footer.</summary>
    public string AboutUrl { get; init; } = "";
}

/// <summary>GET /api/site – what the public pages (privacy, footer) show about the operator.</summary>
public sealed record SiteInfo(string OperatorName, string OperatorEmail, string AboutUrl);

public static class SiteEndpoints
{
    public static IServiceCollection AddSite(this IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton(config.GetSection(OperatorOptions.SectionName).Get<OperatorOptions>() ?? new OperatorOptions());
        return services;
    }

    public static IEndpointRouteBuilder MapSiteEndpoints(this IEndpointRouteBuilder app)
    {
        // Public – the privacy page must be readable before anybody signs in
        app.MapGet("/api/site", (OperatorOptions op) => Results.Ok(new SiteInfo(op.Name, op.Email, op.AboutUrl)))
            .AllowAnonymous();
        return app;
    }
}
