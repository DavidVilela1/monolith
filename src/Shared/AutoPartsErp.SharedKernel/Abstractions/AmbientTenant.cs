namespace AutoPartsErp.SharedKernel.Abstractions;

/// <summary>
/// An explicitly set tenant for work that has no request behind it.
/// <para>
/// Everything in the system reads the tenant through <see cref="ITenantContext"/>, and until now
/// every implementation of that got it from the current HTTP call. That held only because event
/// handlers ran inside the publisher's request. Once they run in a background loop there is no
/// request, no header and no claim — and a tenant that silently resolves to a default is how one
/// company's stock ends up on another company's shelf.
/// </para>
/// <para>
/// Scoped. The outbox processor sets it from the message before invoking a handler; an ordinary
/// request leaves it null and the request-based resolution applies as before.
/// </para>
/// </summary>
public sealed class AmbientTenant
{
    /// <summary>The tenant to use, or null when there is a request to read one from.</summary>
    public Guid? TenantId { get; set; }

    /// <summary>The tenant's code, when one is known.</summary>
    public string? TenantCode { get; set; }

    /// <summary>True when a tenant has been set explicitly.</summary>
    public bool IsSet => TenantId is not null;

    /// <summary>Sets the tenant for the current scope.</summary>
    public void Set(Guid tenantId, string? tenantCode = null)
    {
        TenantId = tenantId;
        TenantCode = tenantCode;
    }

    /// <summary>Clears the tenant.</summary>
    public void Clear()
    {
        TenantId = null;
        TenantCode = null;
    }
}
