using AutoPartsErp.Modules.Access.Application;
using AutoPartsErp.Modules.Access.Domain.Users;
using Microsoft.AspNetCore.Identity;

namespace AutoPartsErp.Modules.Access.Infrastructure.Security;

/// <summary>
/// Hashes passwords with the ASP.NET Core implementation.
/// <para>
/// A thin wrapper over <see cref="PasswordHasher{TUser}"/>, and thin is the entire point. That
/// class is PBKDF2 with HMAC-SHA512, 100,000 iterations and a per-password salt, and it has been
/// read by more people than anything in this repository ever will be. It also carries a version
/// byte, so the day those parameters are no longer enough, rehashing on next sign-in is a
/// supported move rather than a migration.
/// </para>
/// <para>
/// Writing this by hand is the single most expensive mistake available in an ERP. It looks like
/// twenty lines, it passes every test somebody writes for it, and it is discovered to have been
/// wrong on the day a database ends up somewhere it should not be.
/// </para>
/// </summary>
public sealed class AspNetPasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<User> _hasher = new();

    /// <summary>
    /// A hash of a value nobody knows, verified against when there is no such account.
    /// <para>
    /// Computed once at startup rather than per request: the cost that matters is the
    /// verification, and a fresh hash each time would spend the salt generation as well and make
    /// the no-account path measurably <em>slower</em> than the real one.
    /// </para>
    /// </summary>
    private readonly string _dummyHash;

    /// <summary>Initializes the hasher.</summary>
    public AspNetPasswordHasher()
    {
        _dummyHash = _hasher.HashPassword(null!, Guid.NewGuid().ToString("N"));
    }

    /// <inheritdoc />
    public string Hash(string password) => _hasher.HashPassword(null!, password);

    /// <inheritdoc />
    public bool Verify(string hash, string password)
    {
        if (string.IsNullOrEmpty(hash))
        {
            return false;
        }

        PasswordVerificationResult result = _hasher.VerifyHashedPassword(null!, hash, password);

        // SuccessRehashNeeded means the stored hash used older parameters. It is still correct,
        // and upgrading it silently on sign-in is the standard move - not done here, because
        // rewriting a password hash needs a save this method has no business performing.
        return result is PasswordVerificationResult.Success
            or PasswordVerificationResult.SuccessRehashNeeded;
    }

    /// <inheritdoc />
    public void VerifyDummy(string password) =>
        _hasher.VerifyHashedPassword(null!, _dummyHash, password);
}
