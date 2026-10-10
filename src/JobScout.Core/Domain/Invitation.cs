using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace JobScout.Core.Domain;

/// <summary>
/// One-time invitation link (ADR-0011). Only the code's SHA-256 hash is stored – a leaked database leaks no usable
/// invitations. It binds whoever signs in with it first, not an e-mail address.
/// </summary>
public sealed class Invitation
{
    public static readonly TimeSpan ValidFor = TimeSpan.FromDays(14);

    public Guid Id { get; private set; }
    public string CodeHash { get; private set; } = "";
    public string Note { get; private set; } = "";             // for whom – so the admin knows
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? UsedAt { get; private set; }
    public Guid? UsedByUserId { get; private set; }

    private Invitation() { }

    /// <returns>The invitation to store and the code to send – the code itself is never stored.</returns>
    public static (Invitation Invitation, string Code) Create(string note, DateTimeOffset now)
    {
        var code = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));   // 128 random bits, URL-safe
        var invitation = new Invitation
        {
            Id = Guid.CreateVersion7(now),
            CodeHash = HashCode(code),
            Note = note.Trim(),
            CreatedAt = now,
            ExpiresAt = now + ValidFor,
        };
        return (invitation, code);
    }

    public static string HashCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code.Trim())));

    public bool IsUsable(DateTimeOffset now) => UsedAt is null && now < ExpiresAt;

    public void Use(Guid userId, DateTimeOffset now)
    {
        if (!IsUsable(now))
            throw new InvalidOperationException("Pozvánka už byla použita nebo vypršela.");
        UsedAt = now;
        UsedByUserId = userId;
    }
}
