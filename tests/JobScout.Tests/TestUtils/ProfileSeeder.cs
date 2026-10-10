using JobScout.Application.Llm;
using JobScout.Core.Domain;
using JobScout.Infrastructure.Persistence;

namespace JobScout.Tests.TestUtils;

/// <summary>Stores an analysed profile for a user – what AnalyzeProfile would have saved, without calling the AI.</summary>
internal static class ProfileSeeder
{
    public static void Add(JobScoutDbContext db, Guid userId, DateTimeOffset at, int version = 1, params string[] professions)
    {
        var profile = TestProfiles.With(professions.Length == 0 ? new[] { "Softwarový architekt" } : professions);
        db.UserProfiles.Add(new UserProfile(userId, version, "cv.md", null, $"HASH-{version}",
            LlmJson.Serialize(profile), "fake-model", at));
        db.SaveChanges();
    }
}
