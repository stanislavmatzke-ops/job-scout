using System.Reflection;
using System.Runtime.CompilerServices;
using JobScout.Application.Abstractions;
using JobScout.Core.Domain;
using JobScout.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Tests.Architecture;

/// <summary>
/// Architecture fitness functions (ADR-0003, ADR-0010): the layering rules are checked on every build,
/// so they can't erode silently. Each failure message says which rule was broken and by what.
/// </summary>
public class ArchitectureTests
{
    private static readonly Assembly Core = typeof(User).Assembly;
    private static readonly Assembly Application = typeof(IAppDbContext).Assembly;
    private static readonly Assembly Infrastructure = typeof(JobScoutDbContext).Assembly;

    /// <summary>Assemblies a given assembly really uses (only references that made it into the compiled IL).</summary>
    private static IEnumerable<string> ReferencedAssemblies(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!);

    private static IReadOnlyList<string> Forbidden(Assembly assembly, params string[] prefixes) =>
        ReferencedAssemblies(assembly)
            .Where(name => prefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)))
            .ToList();

    // ---------- Direction of dependencies ----------

    [Fact]
    public void Core_depends_on_nothing_but_the_runtime()
    {
        // The domain must not know about use cases, adapters, the database or any vendor SDK
        var forbidden = Forbidden(Core,
            "JobScout.Application", "JobScout.Infrastructure", "JobScout.Worker",
            "Microsoft.EntityFrameworkCore", "Microsoft.Extensions",
            "Anthropic", "AngleSharp", "Microsoft.Playwright");

        Assert.True(forbidden.Count == 0, $"Core must stay pure, but references: {string.Join(", ", forbidden)}");
    }

    [Fact]
    public void Application_does_not_depend_on_infrastructure_or_vendors()
    {
        // Allowed: Core and the EF Core base package (DbSet/LINQ). Not allowed: adapters, providers, SDKs.
        var forbidden = Forbidden(Application,
            "JobScout.Infrastructure", "JobScout.Worker",
            "Microsoft.EntityFrameworkCore.Sqlite", "Microsoft.EntityFrameworkCore.Relational", "Npgsql",
            "Anthropic", "AngleSharp", "Microsoft.Playwright");

        Assert.True(forbidden.Count == 0, $"Application must not know adapters/providers, but references: {string.Join(", ", forbidden)}");
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_hosts()
    {
        var forbidden = Forbidden(Infrastructure, "JobScout.Worker", "JobScout.Api");

        Assert.True(forbidden.Count == 0, $"Infrastructure must not reference hosts, but references: {string.Join(", ", forbidden)}");
    }

    // ---------- Rich domain model ----------

    private static IEnumerable<Type> DomainEntities() =>
        Core.GetTypes().Where(t =>
            t is { IsClass: true, IsAbstract: false, IsNested: false, Namespace: "JobScout.Core.Domain" }
            && !t.IsDefined(typeof(CompilerGeneratedAttribute)));

    [Fact]
    public void Domain_entities_have_no_public_setters()
    {
        // Changes must go through methods that enforce the rules (e.g. ChangeStatus always writes history)
        var offenders = DomainEntities()
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.SetMethod is { IsPublic: true })
                .Select(p => $"{t.Name}.{p.Name}"))
            .ToList();

        Assert.True(offenders.Count == 0, $"Public setters break the rich domain model: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Domain_entities_have_a_non_public_parameterless_constructor_for_ef()
    {
        // EF Core materialises entities through it; the public constructors stay the only way for our code
        var missing = DomainEntities()
            .Where(t => t.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, Type.EmptyTypes) is null)
            .Select(t => t.Name)
            .ToList();

        Assert.True(missing.Count == 0, $"Missing private parameterless constructor: {string.Join(", ", missing)}");
    }

    // ---------- Persistence mapping completeness ----------

    [Fact]
    public void Every_domain_entity_has_an_ef_configuration()
    {
        // Forgetting a configuration means EF guesses: wrong keys (pitfall #1), no unique indexes, no max lengths
        var configured = Infrastructure.GetTypes()
            .SelectMany(t => t.GetInterfaces())
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntityTypeConfiguration<>))
            .Select(i => i.GetGenericArguments()[0])
            .ToHashSet();

        var notConfigured = DomainEntities().Where(t => !configured.Contains(t)).Select(t => t.Name).ToList();

        Assert.True(notConfigured.Count == 0, $"Entities without IEntityTypeConfiguration: {string.Join(", ", notConfigured)}");
    }

    [Fact]
    public void Domain_entity_keys_are_never_generated_by_the_database()
    {
        // ADR-0009: we create Guid v7 ourselves – EF must not treat new entities with an Id as existing ones
        using var db = new JobScoutDbContext(
            new DbContextOptionsBuilder<JobScoutDbContext>().UseSqlite("DataSource=:memory:").Options);

        var generated = db.Model.GetEntityTypes()
            .SelectMany(e => e.FindPrimaryKey()!.Properties.Select(p => (Entity: e.ClrType.Name, Key: p)))
            .Where(x => x.Key.ValueGenerated != Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never)
            .Select(x => $"{x.Entity}.{x.Key.Name}")
            .ToList();

        Assert.True(generated.Count == 0, $"Keys must use ValueGeneratedNever(): {string.Join(", ", generated)}");
    }
}
