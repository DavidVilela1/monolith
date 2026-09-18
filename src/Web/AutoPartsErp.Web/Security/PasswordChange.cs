using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AutoPartsErp.Web.Security;

/// <summary>Claims this browser surface uses that the API's tokens do not.</summary>
public static class ErpWebClaims
{
    /// <summary>
    /// Present while somebody is signed in on a password an administrator chose for them.
    /// <para>
    /// Not in the shared claim set, because it is not a shared idea: an API caller is told
    /// <c>mustChangePassword</c> in the sign-in response and decides what to do about it. A
    /// browser has nowhere to keep that, so it travels in the cookie.
    /// </para>
    /// </summary>
    public const string MustChangePassword = "erp:must_change_password";
}

/// <summary>
/// Sends somebody on a password they did not choose to the screen that changes it, and nowhere
/// else.
/// <para>
/// Registered for every controller rather than written on the ones that matter. The first
/// password in this system comes out of a configuration file — it is in a file on a laptop, and
/// on whatever machine that file was copied to — and a person who can browse the ERP while still
/// on it has been given a warning instead of a gate. A screen added next month must not be able
/// to opt out of this by forgetting an attribute.
/// </para>
/// </summary>
public sealed class RequirePasswordChangeFilter : IAsyncActionFilter
{
    /// <inheritdoc />
    public Task OnActionExecutionAsync(
        ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        // The Account controller is where signing in, signing out and changing the password all
        // live. Redirecting those would be a redirect to itself, and would trap somebody who
        // would rather just sign out.
        bool onAccount = string.Equals(
            context.RouteData.Values["controller"] as string,
            "Account",
            StringComparison.OrdinalIgnoreCase);

        if (onAccount || !context.HttpContext.User.MustChangePassword())
        {
            return next();
        }

        context.Result = new RedirectToActionResult("ChangePassword", "Account", routeValues: null);

        return Task.CompletedTask;
    }
}
