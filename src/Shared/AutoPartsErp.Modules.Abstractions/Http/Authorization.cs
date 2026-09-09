using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace AutoPartsErp.Modules.Abstractions.Http;

/// <summary>
/// The claim names this system puts in a token and reads back out of one.
/// <para>
/// In the shared web project because both ends need them: the Access module signs them in, and
/// every other module's endpoints read them back. A typo in either half is an authorization check
/// that silently passes nothing — or, worse, a tenant that silently falls back to a default.
/// </para>
/// </summary>
public static class ErpClaims
{
    /// <summary>The tenant the token was issued for.</summary>
    public const string TenantId = "tenant_id";

    /// <summary>The tenant's short code, for logs and document numbering.</summary>
    public const string TenantCode = "tenant_code";

    /// <summary>One entry per permission the holder has.</summary>
    public const string Permission = "perm";

    /// <summary>The prefix of the authorization policy created for each permission.</summary>
    public const string PermissionPolicyPrefix = "perm:";
}

/// <summary>Puts a permission requirement on an endpoint or a group of them.</summary>
public static class AuthorizationEndpointExtensions
{
    /// <summary>
    /// Requires the caller's token to carry a permission.
    /// <para>
    /// Applied to a whole route group wherever the group shares one requirement, and per route
    /// where it does not. Reading a stock balance and accepting a stock difference are not the
    /// same act and must not be behind the same check, however convenient the grouping is.
    /// </para>
    /// <para>
    /// The policy name is the permission with a prefix. Policies are not registered one by one —
    /// there is a provider in the API that builds them on demand — because a registration per
    /// permission is a list somebody eventually forgets to add to, and the endpoint whose policy
    /// was never registered fails closed but at startup, on an unrelated deployment.
    /// </para>
    /// </summary>
    /// <param name="builder">The endpoint or group.</param>
    /// <param name="permission">The permission required, from the shared kernel's catalogue.</param>
    /// <typeparam name="TBuilder">Whatever is being decorated.</typeparam>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.RequireAuthorization(ErpClaims.PermissionPolicyPrefix + permission);
    }
}
