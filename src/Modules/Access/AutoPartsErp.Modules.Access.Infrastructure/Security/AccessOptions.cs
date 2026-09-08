using System.ComponentModel.DataAnnotations;

namespace AutoPartsErp.Modules.Access.Infrastructure.Security;

/// <summary>
/// What the token issuer needs to know, from <c>Erp:Access</c> in configuration.
/// </summary>
public sealed class AccessOptions
{
    /// <summary>The configuration section these are read from.</summary>
    public const string SectionName = "Erp:Access";

    /// <summary>
    /// The secret every token is signed with.
    /// <para>
    /// Whoever holds this can mint a token for anybody with any permission, so it belongs in a
    /// secret store or an environment variable and never in a file that goes into version
    /// control. At least 32 bytes: HMAC-SHA256 with a shorter key is refused rather than
    /// quietly weakened.
    /// </para>
    /// </summary>
    [Required]
    [MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Who issued the token. Checked on the way back in.</summary>
    public string Issuer { get; set; } = "AutoPartsErp";

    /// <summary>Who the token is for. Checked on the way back in.</summary>
    public string Audience { get; set; } = "AutoPartsErp";

    /// <summary>
    /// How long an access token is accepted for.
    /// <para>
    /// Short on purpose. A signed token cannot be withdrawn, so this number is exactly how long
    /// somebody keeps working after their access is taken away. Fifteen minutes with a refresh
    /// handle behind it costs nobody anything; eight hours means an ex-employee has the afternoon.
    /// </para>
    /// </summary>
    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>How long a session can be renewed without typing a password again.</summary>
    public int RefreshTokenDays { get; set; } = 14;

    /// <summary>
    /// The address of the first administrator, created on an empty database.
    /// <para>
    /// Only ever used when the users table is empty. A company with one user already has somebody
    /// who can create the rest.
    /// </para>
    /// </summary>
    public string? BootstrapAdminEmail { get; set; }

    /// <summary>
    /// The password for that first administrator.
    /// <para>
    /// The account is created with "must change password" set, so this value is a delivery
    /// mechanism rather than a credential — but until somebody signs in and changes it, it is a
    /// working password sitting in configuration.
    /// </para>
    /// </summary>
    public string? BootstrapAdminPassword { get; set; }
}
