using AutoPartsErp.SharedKernel.Authorization;
using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Access.Domain.Roles;

/// <summary>
/// A named bundle of permissions: what a job involves.
/// <para>
/// Roles exist because permissions are unusable on their own. Nobody wants to tick thirty-six
/// boxes for each new counter clerk, and nobody can look at thirty-six ticked boxes and tell
/// whether they add up to the job. A role is the sentence — "counter sales" — and the
/// permissions are what it means.
/// </para>
/// <para>
/// Permissions live on the role rather than on the user, with one deliberate consequence: giving
/// a person an exception means giving them a second role, not editing their account. That keeps
/// the answer to "why can she do this?" to a list of role names instead of an audit of one
/// person's history, which is the question that actually gets asked.
/// </para>
/// </summary>
public sealed class Role : AggregateRoot<RoleId>, IAuditable, ITenantScoped
{
    /// <summary>Longest permitted role code.</summary>
    public const int MaxCodeLength = 40;

    /// <summary>Longest permitted role name.</summary>
    public const int MaxNameLength = 120;

    /// <summary>Longest permitted description.</summary>
    public const int MaxDescriptionLength = 300;

    private readonly List<RolePermission> _permissions = [];

    private Role(RoleId id, string code, string name, string? description, bool isSystem)
        : base(id)
    {
        Code = code;
        Name = name;
        Description = description;
        IsSystem = isSystem;
        CreatedBy = string.Empty;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private Role()
    {
    }
#pragma warning restore CS8618

    /// <summary>Short uppercase code, unique within the tenant.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>What the role is called on screen.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>What the role is for, for whoever has to decide who gets it.</summary>
    public string? Description { get; private set; }

    /// <summary>
    /// True for roles the system creates and depends on.
    /// <para>
    /// Exactly one of these exists: the administrator. It cannot be deleted or stripped of
    /// <c>access.role.manage</c>, because a company that removes the last person able to grant
    /// permissions has locked itself out of its own ERP with no way back in short of SQL.
    /// </para>
    /// </summary>
    public bool IsSystem { get; private set; }

    /// <summary>
    /// What holders of this role may do.
    /// <para>
    /// A collection of one-property records rather than of bare strings, and it is EF that asks
    /// for that: a primitive collection has no navigation to configure and no reliable mapping
    /// for a set, while an owned collection is the same shape as every other child table in this
    /// system. <see cref="RolePermission.Name"/> is the permission.
    /// </para>
    /// </summary>
    public IReadOnlyCollection<RolePermission> Permissions => _permissions.AsReadOnly();

    /// <summary>The permissions as plain strings, for anything that puts them in a token.</summary>
    public IEnumerable<string> PermissionNames => _permissions.Select(permission => permission.Name);

    /// <inheritdoc />
    public Guid TenantId { get; set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <inheritdoc />
    public string CreatedBy { get; set; } = string.Empty;

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; set; }

    /// <inheritdoc />
    public string? ModifiedBy { get; set; }

    /// <summary>Creates a role.</summary>
    /// <param name="code">Short code, unique within the tenant.</param>
    /// <param name="name">Display name.</param>
    /// <param name="description">What the role is for.</param>
    /// <param name="isSystem">True only for the administrator role the seeder creates.</param>
    public static Result<Role> Create(
        string code,
        string name,
        string? description = null,
        bool isSystem = false)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return AccessErrors.Role.CodeRequired;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return AccessErrors.Role.NameRequired;
        }

        return new Role(
            RoleId.New(),
            code.Trim().ToUpperInvariant()[..Math.Min(code.Trim().Length, MaxCodeLength)],
            name.Trim()[..Math.Min(name.Trim().Length, MaxNameLength)],
            Clean(description, MaxDescriptionLength),
            isSystem);
    }

    /// <summary>Renames the role. The code never changes, because other things quote it.</summary>
    public Result Rename(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return AccessErrors.Role.NameRequired;
        }

        Name = name.Trim()[..Math.Min(name.Trim().Length, MaxNameLength)];
        Description = Clean(description, MaxDescriptionLength);

        return Result.Success();
    }

    /// <summary>
    /// Replaces what the role may do.
    /// <para>
    /// Replaces rather than adds, because a permission list is only reviewable if the screen that
    /// shows it is also the screen that sets it. An add-and-remove API produces roles whose
    /// contents nobody can predict without replaying their history.
    /// </para>
    /// <para>
    /// Unknown permission strings are refused rather than stored. A role holding a permission
    /// that no longer exists grants nothing, which is safe — and looks on screen exactly like a
    /// role that grants something, which is not.
    /// </para>
    /// </summary>
    /// <param name="permissions">The complete set the role should carry.</param>
    public Result SetPermissions(IReadOnlyCollection<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        foreach (string permission in permissions)
        {
            if (!SharedKernel.Authorization.Permissions.IsKnown(permission))
            {
                return AccessErrors.Role.UnknownPermission(permission);
            }
        }

        // The administrator role must keep the right to hand out rights. Losing it is not a
        // mistake somebody notices in time - it is noticed by the next person who needs a
        // password reset, from outside a system nobody can get into.
        if (IsSystem
            && !permissions.Contains(SharedKernel.Authorization.Permissions.Access.ManageRoles))
        {
            return AccessErrors.Role.SystemRoleCannotLosePermissions;
        }

        _permissions.Clear();

        foreach (string permission in permissions.Distinct(StringComparer.Ordinal))
        {
            _permissions.Add(new RolePermission(permission));
        }

        return Result.Success();
    }

    /// <summary>True when this role carries the permission.</summary>
    public bool Grants(string permission) =>
        _permissions.Exists(held => string.Equals(held.Name, permission, StringComparison.Ordinal));

    private static string? Clean(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}

/// <summary>One permission carried by a role.</summary>
/// <param name="Name">
/// The permission, from the catalogue in <c>Permissions</c>. A record with one property
/// rather than a bare string because that is what EF maps as a child table, and a child table is
/// what makes "which roles can issue invoices" a WHERE clause instead of a scan.
/// </param>
public sealed record RolePermission(string Name);
