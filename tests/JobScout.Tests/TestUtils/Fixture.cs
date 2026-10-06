namespace JobScout.Tests.TestUtils;

/// <summary>Reads test HTML pages copied next to the test assembly (see Fixtures\** in the .csproj).</summary>
internal static class Fixture
{
    public static string JobsCz(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "JobsCz", fileName));
}
