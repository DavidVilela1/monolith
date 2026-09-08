using System.Security.Claims;
using AutoPartsErp.Modules.Abstractions.Http;
using AutoPartsErp.SharedKernel.Abstractions;

namespace AutoPartsErp.Api.Infrastructure;

/// <summary>
/// Resolves the tenant for the current request.
/// <para>
/// From a claim inside a validated token, and from nowhere else. There used to be an
/// <c>X-Tenant-Id</c> header here, which was a way of saying "I am company B" that company A
/// could also say — anybody who could reach the API could read anybody's data by changing one
/// header. A claim cannot be edited by whoever is holding the token, because editing it breaks
/// the signature.
/// </para>
/// <para>
/// The default tenant survives, and only for calls with no authenticated user behind them: the
/// health check, the root document, and the seeder that has to create the first administrator
/// before anybody can sign in. A request that carries a token gets that token's tenant or none.
/// </para>
/// <para>
/// An explicitly set <see cref="AmbientTenant"/> still wins over everything. That is how the
/// outbox processor tells a handler which company's data it is working on: there is no request
/// behind a background sweep, and without this the tenant would quietly fall back to the default
/// and one company's goods would be received onto another company's shelf.
/// </para>
/// </summary>
public sealed class HttpTenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _accessor;
    private readonly AmbientTenant _ambient;
    private readonly Guid _defaultTenantId;
    private readonly string _defaultTenantCode;

    /// <summary>Initializes the tenant context.</summary>
    public HttpTenantContext(
        IHttpContextAccessor accessor,
        AmbientTenant ambient,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _accessor = accessor;
        _ambient = ambient;
        _defaultTenantId = configuration.GetValue<Guid?>("Erp:DefaultTenantId")
            ?? Guid.Parse("00000000-0000-0000-0000-000000000001");
        _defaultTenantCode = configuration.GetValue<string>("Erp:DefaultTenantCode") ?? "DEFAULT";
    }

    /// <inheritdoc />
    public Guid TenantId
    {
        get
        {
            if (_ambient.TenantId is { } ambient)
            {
                return ambient;
            }

            HttpContext? context = _accessor.HttpContext;

            if (context is null)
            {
                return _defaultTenantId;
            }

            string? claim = context.User.FindFirstValue(ErpClaims.TenantId);

            return Guid.TryParse(claim, out Guid fromClaim) ? fromClaim : _defaultTenantId;
        }
    }

    /// <inheritdoc />
    public string TenantCode =>
        _ambient.TenantCode
        ?? _accessor.HttpContext?.User.FindFirstValue(ErpClaims.TenantCode)
        ?? _defaultTenantCode;
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
