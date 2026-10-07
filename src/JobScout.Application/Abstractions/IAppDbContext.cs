using JobScout.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Application.Abstractions;

/// <summary>
/// Database as seen by use cases. Implemented by the EF Core context in Infrastructure –
/// the Application layer knows the entities and LINQ, not the database provider. (ADR-0003, ADR-0008)
/// </summary>
public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<UserProfile> UserProfiles { get; }
    DbSet<JobOffer> JobOffers { get; }
    DbSet<JobMatch> JobMatches { get; }
    DbSet<SourceCategory> SourceCategories { get; } 

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}