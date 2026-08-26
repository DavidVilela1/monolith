using System.Security.Claims;
using AutoPartsErp.SharedKernel.Abstractions;

namespace AutoPartsErp.Api.Infrastructure;

/// <summary>
/// Resolves the tenant for the current request.
/// <para>
/// Until authentication is wired up, the tenant comes from an <c>X-Tenant-Id</c> header and
/// falls back to a configured default. The important thing is that the rest of the system
/// already asks for the tenant through this interface, so replacing this with a claim read from
/// a validated token later touches one file rather than every query in the application.
/// </para>
/// </summary>
public sealed class HttpTenantContext : ITenantContext
{
    /// <summary>The header a client uses to select a tenant while authentication is not yet in place.</summary>
    public const string TenantHeader = "X-Tenant-Id";

    /// <summary>The claim the tenant will be read from once tokens are issued.</summary>
    public const string TenantClaim = "tenant_id";

    private readonly IHttpContextAccessor _accessor;
    private readonly Guid _defaultTenantId;

    /// <summary>Initializes the tenant context.</summary>
    public HttpTenantContext(IHttpContextAccessor accessor, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _accessor = accessor;
        _defaultTenantId = configuration.GetValue<Guid?>("Erp:DefaultTenantId")
            ?? Guid.Parse("00000000-0000-0000-0000-000000000001");
        TenantCode = configuration.GetValue<string>("Erp:DefaultTenantCode") ?? "DEFAULT";
    }

    /// <inheritdoc />
    public Guid TenantId
    {
        get
        {
            HttpContext? context = _accessor.HttpContext;

            if (context is null)
            {
                return _defaultTenantId;
            }

            string? claim = context.User.FindFirstValue(TenantClaim);
            if (Guid.TryParse(claim, out Guid fromClaim))
            {
                return fromClaim;
            }

            if (context.Request.Headers.TryGetValue(TenantHeader, out Microsoft.Extensions.Primitives.StringValues header)
                && Guid.TryParse(header.ToString(), out Guid fromHeader))
            {
                return fromHeader;
            }

            return _defaultTenantId;
        }
    }

    /// <inheritdoc />
        public string TenantCode { get; }
}

/// <summary>
/// The identity performing the current request.
/// Falls back to "system" for unauthenticated calls and background work, so the audit trail
/// always records something rather than a null.
/// </summary>
public sealed class HttpCurrentUser : ICurrentUser
{
    private const string SystemUser = "system";

    private readonly IHttpContextAccessor _accessor;

    /// <summary>Initializes the current-user accessor.</summary>
    public HttpCurrentUser(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    /// <inheritdoc />
    public string UserId =>
        _accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? SystemUser;

    /// <inheritdoc />
    public string UserName =>
        _accessor.HttpContext?.User.Identity?.Name ?? SystemUser;

    /// <inheritdoc />
    public bool IsAuthenticated =>
        _accessor.HttpContext?.User.Identity?.IsAuthenticated ?? false;
}
