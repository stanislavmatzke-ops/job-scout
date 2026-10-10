using JobScout.Application.Users;
using JobScout.Core.Domain;
using JobScout.Tests.TestUtils;
using Microsoft.EntityFrameworkCore;

namespace JobScout.Tests.Application.Users;

public sealed class CreateInvitationTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 10, 8, 0, 0, TimeSpan.Zero);
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task Invitation_is_stored_by_hash_and_the_code_is_returned_once()
    {
        CreatedInvitation created;
        await using (var db = _database.CreateContext())
            created = await new CreateInvitation(db, new FixedTimeProvider(T0)).ExecuteAsync("Petr – autolakýrník");

        await using var check = _database.CreateContext();
        var stored = await check.Invitations.SingleAsync();
        Assert.Equal(created.Id, stored.Id);
        Assert.Equal(Invitation.HashCode(created.Code), stored.CodeHash);
        Assert.Equal(T0 + Invitation.ValidFor, created.ExpiresAt);
        Assert.Null(_database.Scalar($"SELECT Id FROM Invitations WHERE CodeHash = '{created.Code}'"));
    }
}
