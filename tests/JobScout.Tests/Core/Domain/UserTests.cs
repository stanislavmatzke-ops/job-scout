using JobScout.Core.Domain;

namespace JobScout.Tests.Core.Domain;

/// <summary>Signed-in user: identity from SSO, contact details that may change.</summary>
public class UserTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 6, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void New_user_has_identity_and_time_ordered_id()
    {
        var user = new User("microsoft", "00000000-0000-0000-1234-abcdefabcdef", "stanislav@example.com", "Stanislav", T0);

        Assert.Equal("microsoft", user.Provider);
        Assert.Equal("00000000-0000-0000-1234-abcdefabcdef", user.Subject);
        Assert.Equal(T0, user.CreatedAt);
        Assert.Equal(7, user.Id.Version);   // Guid v7 – sortable by creation time
    }

    [Fact]
    public void Guid_v7_ids_sort_by_creation_time()
    {
        var older = new User("google", "a", "a@example.com", "A", T0);
        var newer = new User("google", "b", "b@example.com", "B", T0.AddSeconds(1));

        // The timestamp is in the first part of the GUID → string order = time order (good for DB indexes)
        Assert.True(string.CompareOrdinal(older.Id.ToString(), newer.Id.ToString()) < 0);
    }

    [Theory]
    [InlineData(null, "subject")]
    [InlineData("", "subject")]
    [InlineData("google", null)]
    [InlineData("google", "   ")]
    public void Provider_and_subject_are_required(string? provider, string? subject)
    {
        Assert.ThrowsAny<ArgumentException>(() => new User(provider!, subject!, "x@example.com", "X", T0));
    }

    [Fact]
    public void Contact_details_can_change_identity_cannot()
    {
        var user = new User("google", "1234567890", "old@example.com", "Old Name", T0);
        var id = user.Id;

        user.UpdateContact("new@example.com", "New Name");

        Assert.Equal("new@example.com", user.Email);
        Assert.Equal("New Name", user.DisplayName);
        Assert.Equal(id, user.Id);
        Assert.Equal("1234567890", user.Subject);
    }
}
