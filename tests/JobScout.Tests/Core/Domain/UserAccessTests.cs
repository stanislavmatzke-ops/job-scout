using JobScout.Core.Domain;

namespace JobScout.Tests.Core.Domain;

/// <summary>
/// Access by request (ADR-0017): a new user waits (Pending), asks for access, the admin approves.
/// Suspended = can sign in and read, but costs nothing – the reason is shown, they can ask again.
/// </summary>
public class UserAccessTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 10, 8, 0, 0, TimeSpan.Zero);

    private static User NewUser() => new("google", "g-1", "jana@example.com", "Jana", T0);

    [Fact]
    public void New_user_waits_for_access()
    {
        var user = NewUser();

        Assert.Equal(AccessStatus.Pending, user.Access);
        Assert.Null(user.AccessRequestedAt);
        Assert.Null(user.ApprovedAt);
    }

    [Fact]
    public void Request_is_recorded_with_the_message()
    {
        var user = NewUser();

        user.RequestAccess("  Hledám práci jako .NET vývojářka.  ", T0.AddMinutes(5));

        Assert.Equal(T0.AddMinutes(5), user.AccessRequestedAt);
        Assert.Equal("Hledám práci jako .NET vývojářka.", user.AccessRequestMessage);
        Assert.Equal(AccessStatus.Pending, user.Access);
    }

    [Fact]
    public void Too_long_request_message_is_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            NewUser().RequestAccess(new string('x', User.MaxRequestMessageLength + 1), T0));
    }

    [Fact]
    public void Approved_user_cannot_request_again()
    {
        var user = NewUser();
        user.Approve(T0);

        Assert.Throws<InvalidOperationException>(() => user.RequestAccess(null, T0));
    }

    [Fact]
    public void Approval_lets_the_user_in()
    {
        var user = NewUser();
        user.RequestAccess(null, T0);

        user.Approve(T0.AddHours(1));

        Assert.Equal(AccessStatus.Approved, user.Access);
        Assert.Equal(T0.AddHours(1), user.ApprovedAt);
    }

    [Fact]
    public void Suspension_needs_a_reason_the_user_will_see()
    {
        var user = NewUser();
        user.Approve(T0);

        Assert.Throws<ArgumentException>(() => user.Suspend("  ", T0));

        user.Suspend("Z kapacitních důvodů je účet pozastaven.", T0.AddDays(3));
        Assert.Equal(AccessStatus.Suspended, user.Access);
        Assert.Equal("Z kapacitních důvodů je účet pozastaven.", user.SuspendedReason);
        Assert.Equal(T0.AddDays(3), user.SuspendedAt);
    }

    [Fact]
    public void Suspended_user_asks_again_and_approval_clears_the_suspension()
    {
        var user = NewUser();
        user.RequestAccess("první žádost", T0);
        user.Approve(T0);
        user.Suspend("Kapacita", T0.AddDays(3));
        Assert.Null(user.AccessRequestedAt);                    // the old request is done – a new one is needed

        user.RequestAccess("Opravdu hledám práci", T0.AddDays(4));
        Assert.Equal(T0.AddDays(4), user.AccessRequestedAt);

        user.Approve(T0.AddDays(5));
        Assert.Equal(AccessStatus.Approved, user.Access);
        Assert.Null(user.SuspendedReason);
        Assert.Null(user.SuspendedAt);
    }

    [Fact]
    public void Sign_in_time_is_remembered()
    {
        var user = NewUser();

        user.RecordSignIn(T0.AddDays(1));

        Assert.Equal(T0.AddDays(1), user.LastSignInAt);
    }

    // ---------- reject (12 Oct) ----------

    [Fact]
    public void Rejected_request_keeps_the_user_outside_with_the_reason()
    {
        var user = NewUser();
        user.RequestAccess("Prosím.", T0);

        user.Reject("  Z kapacitních důvodů teď nové uživatele nepřijímáme.  ", T0.AddHours(1));

        Assert.Equal(AccessStatus.Pending, user.Access);                // nothing in the app opens
        Assert.Equal("Z kapacitních důvodů teď nové uživatele nepřijímáme.", user.RejectedReason);
        Assert.Equal(T0.AddHours(1), user.RejectedAt);
        Assert.Null(user.AccessRequestedAt);                            // settled – a new request is a new one
    }

    [Fact]
    public void Asking_again_after_a_rejection_clears_the_old_answer()
    {
        var user = NewUser();
        user.RequestAccess(null, T0);
        user.Reject("Ne teď.", T0);

        user.RequestAccess("Zkusím to znovu.", T0.AddDays(1));

        Assert.Null(user.RejectedReason);
        Assert.NotNull(user.AccessRequestedAt);
    }

    [Fact]
    public void A_member_cannot_be_rejected_only_suspended()
    {
        var user = NewUser();
        user.Approve(T0);

        Assert.Throws<InvalidOperationException>(() => user.Reject("Ne.", T0));
    }

    [Fact]
    public void Rejection_needs_a_reason()
    {
        Assert.Throws<ArgumentException>(() => NewUser().Reject("  ", T0));
    }
}
