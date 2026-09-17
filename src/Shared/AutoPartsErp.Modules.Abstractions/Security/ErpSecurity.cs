using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using AutoPartsErp.SharedKernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AutoPartsErp.Modules.Abstractions.Security;

/// <summary>
/// Everything about this system's security that is not "who is calling and what may they do".
/// <para>
/// Authentication and authorization live in Access and in the permission policy provider. This is
/// the layer under them: the transport a request arrives on, what a browser is told it may do with
/// the page, how far a proxy is believed, and how many times a minute somebody may guess at a
/// password. All of it in one place, because the failure mode of security spread across a startup
/// file is a line that gets moved and nobody notices what it was between.
/// </para>
/// </summary>
public static class ErpSecurity
{
    /// <summary>
    /// Registers transport hardening, the sign-in throttle and encryption at rest.
    /// </summary>
    /// <param name="services">The host's services.</param>
    /// <param name="configuration">The host's configuration.</param>
    /// <returns>The same collection.</returns>
    public static IServiceCollection AddErpSecurity(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new ErpSecurityOptions();
        configuration.GetSection(ErpSecurityOptions.SectionName).Bind(options);

        services.AddSingleton(options);
        services.AddSingleton(BuildKeyRing(options.Encryption));
        services.AddSingleton<ISecretProtector, AesGcmSecretProtector>();

        ConfigureForwardedHeaders(services, options);

        services.AddHsts(hsts =>
        {
            hsts.MaxAge = TimeSpan.FromDays(options.HstsDays);
            hsts.IncludeSubDomains = true;
            hsts.Preload = options.HstsPreload;
        });

        services.AddHttpsRedirection(redirection =>
        {
            // 308 rather than the default 307, and permanent rather than temporary: the answer
            // for this host will not change, and a browser that caches it stops sending the first
            // request in clear text at all. A GET of /Parts/Details/<id> over HTTP is that
            // identifier on the wire whatever the redirect says afterwards.
            redirection.RedirectStatusCode = StatusCodes.Status308PermanentRedirect;

            if (options.HttpsPort > 0)
            {
                redirection.HttpsPort = options.HttpsPort;
            }
        });

        AddSignInThrottle(services, options);

        return services;
    }

    /// <summary>
    /// Puts the security layer at the front of the pipeline.
    /// <para>
    /// Order is the whole point and is not interchangeable. The forwarded headers come first,
    /// because everything after them — which scheme this request arrived on, which address it came
    /// from, and therefore whether it is redirected and whose sign-in attempts are being counted —
    /// is a different answer before and after. The response headers go on before anything can
    /// produce a response, error pages included.
    /// </para>
    /// </summary>
    /// <param name="app">The host's pipeline.</param>
    /// <param name="environment">Which environment this is.</param>
    /// <returns>The same pipeline.</returns>
    public static IApplicationBuilder UseErpSecurity(
        this IApplicationBuilder app, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(environment);

        app.UseForwardedHeaders();

        // Not in Development. A browser that has seen this header refuses plain HTTP to
        // localhost until it expires, which would break every other project on the machine that
        // serves one — and it is not undone by turning the header off again.
        if (!environment.IsDevelopment())
        {
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseRateLimiter();

        return app;
    }

    private static void ConfigureForwardedHeaders(
        IServiceCollection services, ErpSecurityOptions options)
    {
        services.Configure<ForwardedHeadersOptions>(forwarded =>
        {
            forwarded.ForwardedHeaders =
                ForwardedHeaders.XForwardedFor
                | ForwardedHeaders.XForwardedProto
                | ForwardedHeaders.XForwardedHost;

            // One hop. The chain in an X-Forwarded-For header is written by whoever is upstream,
            // and everything past the proxy this system actually sits behind is a claim by a
            // caller about itself.
            forwarded.ForwardLimit = 1;

            if (options.TrustedProxies.Count == 0)
            {
                return;
            }

            // Replacing the defaults rather than adding to them: the defaults are loopback, and a
            // deployment that has named its proxy means that proxy and not also anything that can
            // reach the process on 127.0.0.1.
            forwarded.KnownProxies.Clear();
            forwarded.KnownNetworks.Clear();

            foreach (string entry in options.TrustedProxies)
            {
                if (string.IsNullOrWhiteSpace(entry))
                {
                    continue;
                }

                string trimmed = entry.Trim();
                int slash = trimmed.IndexOf('/', StringComparison.Ordinal);

                if (slash < 0)
                {
                    forwarded.KnownProxies.Add(Address(trimmed));
                    continue;
                }

                // Fully qualified: .NET 8 has an IPNetwork of its own in System.Net, and the one
                // this list takes is the other one.
                forwarded.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(
                    Address(trimmed[..slash]),
                    int.Parse(trimmed[(slash + 1)..], CultureInfo.InvariantCulture)));
            }
        });
    }

    private static IPAddress Address(string value) =>
        IPAddress.TryParse(value, out IPAddress? address)
            ? address
            : throw new InvalidOperationException(
                $"'{value}' in {ErpSecurityOptions.SectionName}:TrustedProxies is not an address.");

    /// <summary>
    /// A fixed window per address on the two routes where a password is guessed at.
    /// <para>
    /// The Access module already locks an account after repeated failures, which stops somebody
    /// working through the passwords for one known user. It does nothing about the other shape:
    /// one common password tried against every address in the company, which locks nobody and
    /// succeeds roughly as often as people reuse passwords. That is what this counts.
    /// </para>
    /// <para>
    /// Everything else in the system is unlimited. A warehouse doing a stocktake on a handheld
    /// terminal makes bursts of requests that any sensible global limit would refuse, and being
    /// throttled while counting shelves is how a person decides the system is broken.
    /// </para>
    /// </summary>
    private static void AddSignInThrottle(IServiceCollection services, ErpSecurityOptions options)
    {
        int seconds = (int)TimeSpan.FromMinutes(options.SignInWindowMinutes).TotalSeconds;

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            limiter.OnRejected = (context, _) =>
            {
                context.HttpContext.Response.Headers.RetryAfter =
                    seconds.ToString(CultureInfo.InvariantCulture);

                return ValueTask.CompletedTask;
            };

            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                IsSignIn(context.Request)
                    ? RateLimitPartition.GetFixedWindowLimiter(
                        Caller(context),
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = options.SignInAttempts,
                            Window = TimeSpan.FromMinutes(options.SignInWindowMinutes),

                            // No queue. A sign-in that is refused should be refused now; holding
                            // the request open until a window rolls over is a browser that spins
                            // and a person who clicks again.
                            QueueLimit = 0,
                        })
                    : RateLimitPartition.GetNoLimiter<string>("open"));
        });
    }

    /// <summary>The two ways into this system, and only when something is actually being tried.</summary>
    private static bool IsSignIn(HttpRequest request) =>
        HttpMethods.IsPost(request.Method)
        && (request.Path.StartsWithSegments("/api/access/sign-in", StringComparison.OrdinalIgnoreCase)
            || request.Path.StartsWithSegments("/Account/Login", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Who is being counted.
    /// <para>
    /// The address, which after the forwarded headers above is the browser's and not the proxy's.
    /// Not the email being tried: that is chosen by the caller, so counting it would let somebody
    /// have a fresh allowance for every address in the alphabet.
    /// </para>
    /// </summary>
    private static string Caller(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static EncryptionKeyRing BuildKeyRing(EncryptionOptions encryption)
    {
        if (encryption.Keys.Count == 0)
        {
            throw new InvalidOperationException(
                $"No encryption key is configured. Set {ErpSecurityOptions.SectionName}"
                + $":Encryption:Keys:{encryption.ActiveKeyId} to 32 random bytes, base64 encoded — "
                + $"for example {EncryptionKeyRing.GenerateKey()} — in user secrets or an "
                + "environment variable, never in a file that is committed. Whoever holds it can "
                + "read every encrypted column in the database.");
        }

        return EncryptionKeyRing.FromBase64(
            encryption.ActiveKeyId,
            new Dictionary<string, string>(encryption.Keys, StringComparer.Ordinal));
    }
}
