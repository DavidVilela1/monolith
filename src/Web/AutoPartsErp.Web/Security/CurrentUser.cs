using System.Security.Claims;
using AutoPartsErp.Modules.Abstractions.Http;

namespace AutoPartsErp.Web.Security;

/// <summary>
/// What a view needs to know about whoever is looking at it.
/// <para>
/// A navigation bar that offers a person a screen they will be refused on is worse than one that
/// hides it: the refusal arrives after they have decided to do something, and it looks like the
/// system is broken rather than like they are not allowed. So the menu asks these.
/// </para>
/// </summary>
public static class CurrentUser
{
    /// <summary>True when the person holds a permission.</summary>
    /// <param name="principal">Whoever is signed in.</param>
    /// <param name="permission">The permission, from the shared kernel's catalogue.</param>
    public static bool Can(this ClaimsPrincipal? principal, string permission) =>
        principal?.Identity?.IsAuthenticated == true
        && principal.HasClaim(ErpClaims.Permission, permission);

    /// <summary>True when the person holds any one of several permissions.</summary>
    /// <param name="principal">Whoever is signed in.</param>
    /// <param name="permissions">The permissions.</param>
    public static bool CanAny(this ClaimsPrincipal? principal, params string[] permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        foreach (string permission in permissions)
        {
            if (principal.Can(permission))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// What to call the person on screen.
    /// <para>
    /// Their display name, and their email only if somebody signed in without one — which the
    /// domain does not allow, so the fallback is there to render something rather than to be
    /// reached.
    /// </para>
    /// </summary>
    /// <param name="principal">Whoever is signed in.</param>
    public static string DisplayName(this ClaimsPrincipal? principal) =>
        principal?.FindFirstValue(ClaimTypes.Name)
        ?? principal?.FindFirstValue(ClaimTypes.Email)
        ?? string.Empty;
}
