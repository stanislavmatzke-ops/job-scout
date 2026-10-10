using JobScout.Core.Domain;

namespace JobScout.Tests.Core.Domain;

/// <summary>
/// Invite-only (ADR-0011): a one-time link with a random code. Only the code's hash is stored –
/// a leaked database doesn't leak usable invitations. It binds whoever signs in with it, not an e-mail address.
/// </summary>
public class InvitationTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 10, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void New_invitation_has_a_random_code_and_stores_only_its_hash()
    {
        var (invitation, code) = Invitation.Create("Petr – autolakýrník", T0);

        Assert.True(code.Length >= 20);
        Assert.Equal(Invitation.HashCode(code), invitation.CodeHash);
        Assert.DoesNotContain(code, invitation.CodeHash);
        Assert.Equal(T0 + Invitation.ValidFor, invitation.ExpiresAt);
        Assert.Equal("Petr – autolakýrník", invitation.Note);
        Assert.True(invitation.IsUsable(T0));
    }

    [Fact]
    public void Two_invitations_never_share_a_code()
    {
        var (_, first) = Invitation.Create("A", T0);
        var (_, second) = Invitation.Create("B", T0);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Code_is_safe_in_a_url()
    {
        var (_, code) = Invitation.Create("A", T0);

        Assert.Equal(code, Uri.EscapeDataString(code));
    }

    [Fact]
    public void Invitation_can_be_used_once()
    {
        var (invitation, _) = Invitation.Create("Petr", T0);
        var userId = Guid.NewGuid();

        invitation.Use(userId, T0.AddHours(1));

        Assert.Equal((userId, T0.AddHours(1)), (invitation.UsedByUserId!.Value, invitation.UsedAt!.Value));
        Assert.False(invitation.IsUsable(T0.AddHours(2)));
        Assert.Throws<InvalidOperationException>(() => invitation.Use(Guid.NewGuid(), T0.AddHours(2)));
    }

    [Fact]
    public void Expired_invitation_cannot_be_used()
    {
        var (invitation, _) = Invitation.Create("Petr", T0);
        var later = T0 + Invitation.ValidFor;

        Assert.False(invitation.IsUsable(later));
        Assert.Throws<InvalidOperationException>(() => invitation.Use(Guid.NewGuid(), later));
    }
}
