using AutoPartsErp.Modules.Access.Domain.Users;

namespace AutoPartsErp.Modules.Access.Application;

/// <summary>
/// Turns a password into a hash and checks one against another.
/// <para>
/// An abstraction with exactly one implementation, and the reason is not testability. It is that
/// the two cryptographic jobs in this system should be findable: everything a reviewer needs to
/// look at hard lives behind this interface and <see cref="IAccessTokenIssuer"/>, and neither the
/// domain nor the use cases can accidentally do any of it themselves.
/// </para>
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Hashes a password for storage.</summary>
    string Hash(string password);

    /// <summary>True when the password matches the stored hash.</summary>
    bool Verify(string hash, string password);

    /// <summary>
    /// Verifies against a throwaway hash, to spend the same time a real check would.
    /// <para>
    /// Called when there is no such account. Without it, sign-in answers unknown addresses
    /// faster than known ones and the timing tells an attacker which addresses exist — which is
    /// exactly what the single shared error message is there to prevent.
    /// </para>
    /// </summary>
    void VerifyDummy(string password);
}

/// <summary>Issues and validates the tokens that carry identity between calls.</summary>
public interface IAccessTokenIssuer
{
    /// <summary>How long a session may be renewed for without a password.</summary>
    TimeSpan RefreshTokenLifetime { get; }

    /// <summary>Signs an access token carrying who the user is and what they may do.</summary>
    /// <param name="user">Who is signing in.</param>
    /// <param name="permissions">What they may do, already resolved from their roles.</param>
    /// <param name="now">The current instant.</param>
    IssuedAccessToken Issue(User user, IReadOnlyCollection<string> permissions, DateTimeOffset now);

    /// <summary>Generates a refresh handle: the value for the client and the hash to store.</summary>
    IssuedRefreshToken IssueRefreshToken();

    /// <summary>Hashes a refresh handle the same way, so a presented one can be looked up.</summary>
    string HashRefreshToken(string token);
}

/// <summary>A signed access token and when it stops being accepted.</summary>
/// <param name="Token">The token itself.</param>
/// <param name="ExpiresAtUtc">When it expires.</param>
public sealed record IssuedAccessToken(string Token, DateTimeOffset ExpiresAtUtc);

/// <summary>
/// A refresh handle in its two forms.
/// </summary>
/// <param name="Token">What the client is given. Never stored.</param>
/// <param name="Hash">What is stored. Never leaves the server.</param>
public sealed record IssuedRefreshToken(string Token, string Hash);
