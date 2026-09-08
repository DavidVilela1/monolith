using AutoPartsErp.SharedKernel.Primitives;

namespace AutoPartsErp.Modules.Access.Domain.Sessions;

/// <summary>
/// A stored, hashed handle that lets a signed-in person get a fresh access token without typing
/// their password again.
/// <para>
/// This exists because of a trade-off with no comfortable middle. An access token is a signed
/// claim that cannot be withdrawn before it expires — deactivating somebody at nine o'clock does
/// nothing until their token runs out. Long-lived tokens make that window a working day; short
/// ones make people sign in every twenty minutes. A short access token plus one of these gives a
/// short window and a long session, at the cost of one database row per sign-in.
/// </para>
/// <para>
/// The value is stored hashed, exactly like a password, because it <em>is</em> a password: a
/// database somebody can read would otherwise be a database of live sessions.
/// </para>
/// <para>
/// Rotating: using one immediately replaces it. If a handle is ever presented twice, the second
/// presentation is either a stolen copy or a client bug, and both deserve every session that
/// user has to be thrown away rather than quietly renewed.
/// </para>
/// </summary>
public sealed class RefreshToken : Entity<Guid>, ITenantScoped
{
    private RefreshToken(
        Guid id,
        UserId userId,
        string tokenHash,
        DateTimeOffset issuedAtUtc,
        DateTimeOffset expiresAtUtc)
        : base(id)
    {
        UserId = userId;
        TokenHash = tokenHash;
        IssuedAtUtc = issuedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private RefreshToken()
    {
    }
#pragma warning restore CS8618

    /// <summary>Whose session this is.</summary>
    public UserId UserId { get; private set; }

    /// <summary>The hash of the handle. The handle itself is only ever in the client's hands.</summary>
    public string TokenHash { get; private set; } = string.Empty;

    /// <summary>When the session started.</summary>
    public DateTimeOffset IssuedAtUtc { get; private set; }

    /// <summary>When it stops working, whatever else happens.</summary>
    public DateTimeOffset ExpiresAtUtc { get; private set; }

    /// <summary>When it was exchanged for a new one, or withdrawn.</summary>
    public DateTimeOffset? RevokedAtUtc { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; set; }

    /// <summary>True while it can still be exchanged.</summary>
    public bool IsUsable(DateTimeOffset now) => RevokedAtUtc is null && ExpiresAtUtc > now;

    /// <summary>Issues a handle for a session.</summary>
    /// <param name="userId">Whose session.</param>
    /// <param name="tokenHash">The hash of the handle handed to the client.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="lifetime">How long the session may last without a password.</param>
    public static RefreshToken Issue(
        UserId userId,
        string tokenHash,
        DateTimeOffset now,
        TimeSpan lifetime) =>
        new(Guid.NewGuid(), userId, tokenHash, now, now.Add(lifetime));

    /// <summary>Marks it used or withdrawn. Either way it never works again.</summary>
    public void Revoke(DateTimeOffset now) => RevokedAtUtc ??= now;
}
