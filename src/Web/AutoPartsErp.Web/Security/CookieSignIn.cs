using System.Security.Claims;
using AutoPartsErp.Modules.Abstractions.Http;
using AutoPartsErp.Modules.Access.Application.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;

namespace AutoPartsErp.Web.Security;

/// <summary>
/// Turns a successful sign-in into the cookie a browser carries.
/// <para>
/// The password, the lockout, the permissions — all of it is the Access module's work, reached
/// through the same command the API's sign-in route uses. What is different here is only what
/// happens with the answer: a browser gets a cookie the server signs and reads back, rather than
/// a bearer token the page would have to store somewhere a script can reach.
/// </para>
/// <para>
/// <b>The token is thrown away.</b> A sign-in hands back an access token and a refresh handle,
/// and a server-rendered app needs neither: it calls the modules in this same process. Keeping
/// them would mean storing a credential in a cookie for no purpose at all.
/// </para>
/// </summary>
public static class CookieSignIn
{
    /// <summary>
    /// Signs the person in for this browser.
    /// </summary>
    /// <param name="context">The request.</param>
    /// <param name="result">What the Access module said.</param>
    /// <param name="tenantId">The tenant they belong to.</param>
    /// <param name="tenantCode">Its short code.</param>
    /// <param name="stayTrusted">
    /// True to keep the cookie after the browser closes. Off by default: a shared counter
    /// terminal is the ordinary case in a parts business, and the ordinary case should be the
    /// safe one.
    /// </param>
    public static async Task SignInAsync(
        this HttpContext context,
        SignInResult result,
        Guid tenantId,
        string tenantCode,
        bool stayTrusted = false)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(result);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, result.UserId.ToString()),
            new(ClaimTypes.Name, result.DisplayName),
            new(ErpClaims.TenantId, tenantId.ToString()),
            new(ErpClaims.TenantCode, tenantCode),
        };

        // One claim per permission, the same shape the token carries, because the policies that
        // read them are the same policies.
        foreach (string permission in result.Permissions)
        {
            claims.Add(new Claim(ErpClaims.Permission, permission));
        }

        // Carried in the cookie rather than looked up per request. The alternative is a query
        // against the users table on every page, to answer a question whose answer changes once.
        if (result.MustChangePassword)
        {
            claims.Add(new Claim(ErpWebClaims.MustChangePassword, "true"));
        }

        var identity = new ClaimsIdentity(
            claims, CookieAuthenticationDefaults.AuthenticationScheme);

        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = stayTrusted });
    }

    /// <summary>
    /// Reissues the cookie without the must-change-password mark, once they have changed it.
    /// <para>
    /// The same identity minus one claim, rather than signing in again: signing in again would
    /// mean asking for the password a second time, and the permissions are already here. The
    /// "keep me signed in" choice they made is read back off the existing cookie and kept, so
    /// changing a password does not quietly turn a trusted browser into a session one.
    /// </para>
    /// </summary>
    /// <param name="context">The request.</param>
    public static async Task ClearPasswordChangeAsync(this HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        AuthenticateResult existing = await context.AuthenticateAsync(
            CookieAuthenticationDefaults.AuthenticationScheme);

        if (!existing.Succeeded || existing.Principal is null)
        {
            return;
        }

        List<Claim> claims =
        [
            .. existing.Principal.Claims.Where(claim =>
                !string.Equals(claim.Type, ErpWebClaims.MustChangePassword, StringComparison.Ordinal)),
        ];

        var identity = new ClaimsIdentity(
            claims, CookieAuthenticationDefaults.AuthenticationScheme);

        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            existing.Properties ?? new AuthenticationProperties());
    }

    /// <summary>Signs the person out of this browser.</summary>
    /// <param name="context">The request.</param>
    public static Task SignOutAsync(this HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
