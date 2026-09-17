using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace AutoPartsErp.Modules.Abstractions.Security;

/// <summary>
/// The headers that tell a browser what this application is allowed to do, on every response.
/// <para>
/// Razor already encodes everything it renders, which is what stops a part description containing
/// a script tag from becoming one. These headers are the second line: if something ever does get
/// injected — through a view that used raw HTML, a library, a stale cached page — the browser is
/// under instructions not to run it.
/// </para>
/// </summary>
internal sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _policy;
    private readonly bool _reportOnly;

    public SecurityHeadersMiddleware(RequestDelegate next, ErpSecurityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _next = next;
        _policy = BuildPolicy(options);
        _reportOnly = options.ContentSecurityPolicyReportOnly;
    }

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IHeaderDictionary headers = context.Response.Headers;

        // A response whose content type says text/plain and whose body is HTML is a response some
        // browsers will render as HTML. This is the one header that has no downside anywhere.
        headers.XContentTypeOptions = "nosniff";

        // Nothing about this system's URLs travels to another site: a path like
        // /Parts/Details/<id> in a referer header is a part identifier handed to whoever the user
        // clicked through to.
        headers["Referrer-Policy"] = new StringValues("same-origin");

        // frame-ancestors in the policy below says the same thing to anything modern; this is for
        // what is not.
        headers.XFrameOptions = "DENY";

        // Explicitly off. The legacy XSS auditor is a filter that could itself be used to break a
        // page, and every browser that still reads this header does better without it.
        headers.XXSSProtection = "0";

        headers["Permissions-Policy"] = new StringValues(
            "accelerometer=(), camera=(), display-capture=(), geolocation=(), gyroscope=(), "
            + "magnetometer=(), microphone=(), payment=(), usb=()");

        headers["Cross-Origin-Opener-Policy"] = new StringValues("same-origin");
        headers["Cross-Origin-Resource-Policy"] = new StringValues("same-origin");

        // Swagger's page is built from inline script and inline style, and it is only mapped in
        // Development. Sending it a policy it cannot satisfy would break the one page in this
        // system whose whole purpose is to be poked at by hand.
        if (!context.Request.Path.StartsWithSegments("/swagger", StringComparison.OrdinalIgnoreCase))
        {
            if (_reportOnly)
            {
                headers.ContentSecurityPolicyReportOnly = _policy;
            }
            else
            {
                headers.ContentSecurityPolicy = _policy;
            }
        }

        return _next(context);
    }

    /// <summary>
    /// Builds the content security policy.
    /// <para>
    /// Everything comes from this host. The one exception is the origin assets are served from,
    /// which is configuration rather than a constant precisely so it can become nothing.
    /// </para>
    /// </summary>
    private static string BuildPolicy(ErpSecurityOptions options)
    {
        string assets = string.IsNullOrWhiteSpace(options.AssetOrigins)
            ? "'self'"
            : string.Concat("'self' ", options.AssetOrigins.Trim());

        string[] directives =
        [
            "default-src 'self'",

            // A page cannot be re-based, so a relative URL in it cannot be made to point
            // somewhere else by an injected <base>.
            "base-uri 'self'",

            // A form on a page of this system posts to this system. Without it, an injected form
            // could collect a password and post it anywhere.
            "form-action 'self'",

            // Nobody frames this. Clickjacking a "Confirmar" button on an ERP is a real invoice.
            "frame-ancestors 'none'",
            "frame-src 'none'",
            "object-src 'none'",

            $"script-src {assets}",
            $"style-src {assets}",

            // Bootstrap positions dropdowns and tooltips by writing a style attribute, which a
            // policy counts as inline style. Allowing it on attributes only leaves <style> blocks
            // and stylesheets restricted, which is where an injected payload would have to live.
            "style-src-attr 'unsafe-inline'",

            $"font-src {assets}",

            // data: for the small inline icons a page builds; no remote images, so a part
            // description cannot become a beacon that tells somebody it was opened.
            "img-src 'self' data:",

            "connect-src 'self'",
            "manifest-src 'self'",
            "worker-src 'self'",

            // Anything still written as http:// in a page is fetched over https:// instead,
            // rather than becoming a mixed-content warning nobody reads.
            "upgrade-insecure-requests",
        ];

        return string.Join("; ", directives);
    }
}
