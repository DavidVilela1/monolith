using AutoPartsErp.Modules.Abstractions.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace AutoPartsErp.Api.Infrastructure;

/// <summary>
/// Builds an authorization policy for each permission, on demand.
/// <para>
/// The alternative is registering thirty-six policies by hand at startup, which is a list
/// somebody eventually forgets to add to. When that happens the endpoint does not fail open — it
/// throws at first request, complaining about a policy that does not exist, which is safe and is
/// also a production incident caused by a typo. This makes the policy name the whole
/// specification: <c>perm:invoicing.document.issue</c> means "the token must carry that claim",
/// and there is nothing to keep in step.
/// </para>
/// <para>
/// Anything that is not a permission policy falls through to the default provider, so
/// <c>RequireAuthorization()</c> with no argument still means what it always meant.
/// </para>
/// </summary>
public sealed class PermissionPolicyProvider : DefaultAuthorizationPolicyProvider
{
    /// <summary>Initializes the provider.</summary>
    /// <param name="options">The authorization options, as configured at startup.</param>
    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
        : base(options)
    {
    }

    /// <inheritdoc />
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        ArgumentNullException.ThrowIfNull(policyName);

        if (!policyName.StartsWith(ErpClaims.PermissionPolicyPrefix, StringComparison.Ordinal))
        {
            return await base.GetPolicyAsync(policyName).ConfigureAwait(false);
        }

        string permission = policyName[ErpClaims.PermissionPolicyPrefix.Length..];

        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireClaim(ErpClaims.Permission, permission)
            .Build();
    }
}
