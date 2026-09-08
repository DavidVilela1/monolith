using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AutoPartsErp.Modules.Abstractions.Http;
using AutoPartsErp.Modules.Access.Application;
using AutoPartsErp.Modules.Access.Domain.Users;
using AutoPartsErp.SharedKernel.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AutoPartsErp.Modules.Access.Infrastructure.Security;

/// <summary>
/// Signs the tokens that carry identity between calls.
/// <para>
/// HMAC-SHA256 with a shared secret, which is the right choice for a monolith and would be the
/// wrong one the day something else has to validate these tokens without being trusted to issue
/// them. That day the change is here and in the API's validation parameters: swap to RSA, publish
/// the public half, and nothing that reads a claim notices.
/// </para>
/// <para>
/// The permissions go into the token as one claim each rather than a single joined string.
/// Repeated claims are what the authorization stack already understands, so a policy asking
/// "does this token carry <c>invoicing.document.issue</c>" is a lookup rather than a parse — and
/// a parse is where somebody eventually writes a <c>Contains</c> that matches a longer permission
/// with the same prefix.
/// </para>
/// </summary>
public sealed class JwtAccessTokenIssuer : IAccessTokenIssuer
{
    private readonly AccessOptions _options;
    private readonly ITenantContext _tenantContext;
    private readonly SigningCredentials _credentials;

    /// <summary>Initializes the issuer.</summary>
    public JwtAccessTokenIssuer(IOptions<AccessOptions> options, ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _tenantContext = tenantContext;

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        _credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    }

    /// <inheritdoc />
    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(_options.RefreshTokenDays);

    /// <inheritdoc />
    public IssuedAccessToken Issue(
        User user,
        IReadOnlyCollection<string> permissions,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(permissions);

        DateTimeOffset expires = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.DisplayName),
            new(JwtRegisteredClaimNames.Email, user.Email),

            // The tenant travels in the token and nowhere else. It is the whole reason the
            // X-Tenant-Id header could be removed: a claim inside a signed token cannot be
            // changed by whoever is holding it, and a header can.
            new(ErpClaims.TenantId, user.TenantId.ToString()),
            new(ErpClaims.TenantCode, _tenantContext.TenantCode),
        };

        claims.AddRange(permissions.Select(permission => new Claim(ErpClaims.Permission, permission)));

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: _credentials);

        return new IssuedAccessToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    /// <inheritdoc />
    /// <remarks>
    /// 32 bytes from the cryptographic generator, base64url-encoded. Not a <c>Guid</c>: a Guid is
    /// 122 bits of which some implementations make rather fewer unpredictable, and a refresh
    /// handle is a bearer credential worth a fortnight of somebody's access.
    /// </remarks>
    public IssuedRefreshToken IssueRefreshToken()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(32);
        string token = Base64UrlEncoder.Encode(bytes);

        return new IssuedRefreshToken(token, HashRefreshToken(token));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Plain SHA-256, not the password hasher, and the difference matters. A password is short,
    /// human-chosen and guessable, so its hash must be slow on purpose. This is 256 bits of
    /// randomness that nothing can guess, so the hash only has to be one-way — and it is looked
    /// up on every renewal, where a deliberately slow hash would be a self-inflicted delay.
    /// </remarks>
    public string HashRefreshToken(string token) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));
}
