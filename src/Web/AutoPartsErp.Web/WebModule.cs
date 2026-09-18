using System.Globalization;
using AutoPartsErp.Web.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AutoPartsErp.Web;

/// <summary>
/// The browser surface, registered into the host the same way a module is.
/// <para>
/// It calls the modules in this process rather than the API over HTTP. That is what a modular
/// monolith is for: a request to itself would pay serialization and a network hop for data
/// already in memory, and would need a bearer token stored somewhere a script could read.
/// The API stays where it is, for integrations and whatever comes after this.
/// </para>
/// </summary>
public static class WebModule
{
    /// <summary>The cookie a signed-in browser carries.</summary>
    public const string CookieScheme = CookieAuthenticationDefaults.AuthenticationScheme;

    /// <summary>
    /// The scheme that decides which of the other two a request belongs to.
    /// <para>
    /// One host serves both a browser and an API. A browser that is refused should be sent to a
    /// sign-in page; an API caller should get a 401 with nothing to click on. The choice is made
    /// once, by path, rather than repeated on every controller and every endpoint.
    /// </para>
    /// </summary>
    public const string SelectorScheme = "erp";

    /// <summary>Adds the browser surface.</summary>
    /// <param name="services">The host's services.</param>
    public static IServiceCollection AddErpWeb(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Every POST, not the ones somebody remembered to mark. A screen added later that changes
        // something and forgets the attribute is a screen another site can submit on behalf of
        // whoever is signed in — and the cookie below travels on that request, because a form post
        // from another origin is exactly what SameSite=Lax still allows through on navigation.
        services.AddControllersWithViews(mvc =>
        {
            mvc.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());

            // Also global, and for the same reason: a screen built next month cannot forget to
            // keep somebody off it while they are still on the password an administrator gave
            // them.
            mvc.Filters.Add<RequirePasswordChangeFilter>();
        });

        services.AddAntiforgery(antiforgery =>
        {
            antiforgery.Cookie.Name = "erp.antiforgery";
            antiforgery.Cookie.HttpOnly = true;
            antiforgery.Cookie.SecurePolicy = CookieSecurePolicy.Always;

            // Strict, unlike the session cookie: this one is never needed on a navigation that
            // arrives from somewhere else, so there is nothing to trade away.
            antiforgery.Cookie.SameSite = SameSiteMode.Strict;

            // The header exists for the day a screen posts with fetch instead of a form.
            antiforgery.HeaderName = "X-Erp-Antiforgery";
        });

        // Portuguese, and only Portuguese. A parts business in Portugal reads 1.234,56 and
        // 17-09-2026, and a screen that rendered 1,234.56 because the server's locale is English
        // is one where somebody eventually transcribes the wrong figure onto an order.
        services.Configure<RequestLocalizationOptions>(options =>
        {
            var portuguese = new CultureInfo("pt-PT");

            options.DefaultRequestCulture = new RequestCulture(portuguese);
            options.SupportedCultures = [portuguese];
            options.SupportedUICultures = [portuguese];

            // No culture from the browser. The company's books are in one language and one
            // currency, and letting a laptop set to en-US change how a price renders is a bug
            // waiting for somebody to bring their own machine.
            options.RequestCultureProviders.Clear();
        });

        return services;
    }

    /// <summary>Maps the browser surface.</summary>
    /// <param name="app">The host's route builder.</param>
    public static IEndpointRouteBuilder MapErpWeb(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=Index}/{id?}");

        return app;
    }
}
