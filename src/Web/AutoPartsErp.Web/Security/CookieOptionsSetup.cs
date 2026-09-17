using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;

namespace AutoPartsErp.Web.Security;

/// <summary>How the browser's session cookie behaves.</summary>
public static class CookieOptionsSetup
{
    /// <summary>Applies the settings a session cookie for this system should have.</summary>
    /// <param name="options">The cookie options.</param>
    public static void Configure(CookieAuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.Cookie.Name = "erp.session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;

        // Always over HTTPS. An ERP behind a reverse proxy on a local network is still an ERP,
        // and a session cookie that can travel in clear text is one somebody on the same wifi
        // can take.
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;

        // The whole application and nothing narrower, and essential: a session cookie is not
        // something a consent banner may switch off, because switching it off is signing out.
        options.Cookie.Path = "/";
        options.Cookie.IsEssential = true;

        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/Denied";
        options.ReturnUrlParameter = "returnUrl";

        // Eight hours, sliding: a working day, renewed while somebody is working. The access
        // token's fifteen minutes exist because a bearer token cannot be withdrawn; a cookie
        // can, so the trade is different.
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    }
}
