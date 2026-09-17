using AutoPartsErp.Modules.Abstractions.Http;
using Microsoft.AspNetCore.Authorization;

namespace AutoPartsErp.Web.Security;

/// <summary>
/// Requires the signed-in person to hold a permission, on a controller or one action.
/// <para>
/// The same policies the API's routes use: the host has a provider that builds one per
/// permission on demand, so nothing here registers anything. Two surfaces onto one system have
/// to answer "may this person do this?" the same way, and the way to be sure of that is for
/// there to be one answer rather than two that agree today.
/// </para>
/// </summary>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    /// <summary>Requires the permission.</summary>
    /// <param name="permission">The permission, from the shared kernel's catalogue.</param>
    public RequirePermissionAttribute(string permission)
        : base(ErpClaims.PermissionPolicyPrefix + permission)
    {
        Permission = permission;
    }

    /// <summary>The permission required.</summary>
    public string Permission { get; }
}
