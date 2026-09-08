using AutoPartsErp.Modules.Access.Domain;
using AutoPartsErp.Modules.Access.Domain.Roles;
using AutoPartsErp.Modules.Access.Domain.Users;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Access.Application.Administration;

/// <summary>Creates a role.</summary>
/// <param name="Code">Short code, unique within the company.</param>
/// <param name="Name">What it is called on screen.</param>
/// <param name="Description">What the role is for.</param>
/// <param name="Permissions">What holders of it may do.</param>
public sealed record CreateRoleCommand(
    string Code,
    string Name,
    string? Description,
    IReadOnlyList<string> Permissions) : ICommand<Guid>;

/// <summary>Replaces what a role may do.</summary>
/// <param name="RoleId">The role.</param>
/// <param name="Permissions">The complete set it should carry.</param>
public sealed record SetRolePermissionsCommand(
    Guid RoleId,
    IReadOnlyList<string> Permissions) : ICommand;

/// <summary>Renames a role. The code never changes.</summary>
/// <param name="RoleId">The role.</param>
/// <param name="Name">Its new name.</param>
/// <param name="Description">What it is for.</param>
public sealed record RenameRoleCommand(Guid RoleId, string Name, string? Description) : ICommand;

/// <summary>Deletes a role nobody holds.</summary>
/// <param name="RoleId">The role.</param>
public sealed record DeleteRoleCommand(Guid RoleId) : ICommand;

/// <summary>The four things that can be done to a role.</summary>
public sealed class RoleCommandHandler
    : ICommandHandler<CreateRoleCommand, Guid>,
      ICommandHandler<SetRolePermissionsCommand>,
      ICommandHandler<RenameRoleCommand>,
      ICommandHandler<DeleteRoleCommand>
{
    private readonly IRoleRepository _roles;
    private readonly IAccessUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public RoleCommandHandler(IRoleRepository roles, IAccessUnitOfWork unitOfWork)
    {
        _roles = roles;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> HandleAsync(
        CreateRoleCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string code = (request.Code ?? string.Empty).Trim().ToUpperInvariant();

        Role? existing = await _roles.FindByCodeAsync(code, cancellationToken).ConfigureAwait(false);

        if (existing is not null)
        {
            return Result.Failure<Guid>(AccessErrors.Role.CodeAlreadyExists(code));
        }

        Result<Role> role = Role.Create(
            request.Code ?? string.Empty, request.Name ?? string.Empty, request.Description);

        if (role.IsFailure)
        {
            return Result.Failure<Guid>(role.Error);
        }

        Result permissions = role.Value.SetPermissions(request.Permissions ?? []);

        if (permissions.IsFailure)
        {
            return Result.Failure<Guid>(permissions.Error);
        }

        _roles.Add(role.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return role.Value.Id.Value;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Takes effect for anybody already signed in only when their access token expires. That
    /// window is the token's lifetime and nothing longer, which is why the lifetime is short —
    /// but it is a real window, and somebody removing a permission in a hurry should be told the
    /// change is not instant.
    /// </remarks>
    public async Task<Result> HandleAsync(
        SetRolePermissionsCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Role? role = await _roles
            .GetByIdAsync(new RoleId(request.RoleId), cancellationToken)
            .ConfigureAwait(false);

        if (role is null)
        {
            return AccessErrors.Role.NotFound(request.RoleId.ToString());
        }

        Result set = role.SetPermissions(request.Permissions ?? []);

        if (set.IsFailure)
        {
            return set;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        RenameRoleCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Role? role = await _roles
            .GetByIdAsync(new RoleId(request.RoleId), cancellationToken)
            .ConfigureAwait(false);

        if (role is null)
        {
            return AccessErrors.Role.NotFound(request.RoleId.ToString());
        }

        Result renamed = role.Rename(request.Name ?? string.Empty, request.Description);

        if (renamed.IsFailure)
        {
            return renamed;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Refused while anybody holds it. Deleting a role out from under its holders takes access
    /// away from people who will find out one at a time, each of them convinced something else
    /// is broken.
    /// </remarks>
    public async Task<Result> HandleAsync(
        DeleteRoleCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var id = new RoleId(request.RoleId);

        Role? role = await _roles.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (role is null)
        {
            return AccessErrors.Role.NotFound(request.RoleId.ToString());
        }

        if (role.IsSystem)
        {
            return AccessErrors.Role.SystemRoleCannotBeDeleted;
        }

        int holders = await _roles.CountHoldersAsync(id, cancellationToken).ConfigureAwait(false);

        if (holders > 0)
        {
            return AccessErrors.Role.StillHeld(holders);
        }

        _roles.Remove(role);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Every permission this system has, for a screen that builds a role.</summary>
public sealed record ListPermissionsQuery : IQuery<IReadOnlyList<string>>;

/// <summary>Answers from the catalogue in the domain. There is no table behind this.</summary>
public sealed class ListPermissionsQueryHandler
    : IQueryHandler<ListPermissionsQuery, IReadOnlyList<string>>
{
    /// <inheritdoc />
    public Task<Result<IReadOnlyList<string>>> HandleAsync(
        ListPermissionsQuery request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success<IReadOnlyList<string>>(
            [.. Permissions.All.Order(StringComparer.Ordinal)]));
}

/// <summary>The roles in this company.</summary>
public sealed record ListRolesQuery : IQuery<IReadOnlyList<RoleSummary>>;

/// <summary>Serves the role list.</summary>
public sealed class ListRolesQueryHandler : IQueryHandler<ListRolesQuery, IReadOnlyList<RoleSummary>>
{
    private readonly IRoleRepository _roles;

    /// <summary>Initializes the handler.</summary>
    public ListRolesQueryHandler(IRoleRepository roles)
    {
        _roles = roles;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<RoleSummary>>> HandleAsync(
        ListRolesQuery request,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Role> roles = await _roles.ListAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success<IReadOnlyList<RoleSummary>>(
            [.. roles.Select(role => new RoleSummary(
                role.Id.Value,
                role.Code,
                role.Name,
                role.Description,
                role.IsSystem,
                [.. role.PermissionNames.Order(StringComparer.Ordinal)]))]);
    }
}

/// <summary>The users in this company.</summary>
public sealed record ListUsersQuery : IQuery<IReadOnlyList<UserSummary>>;

/// <summary>Serves the user list.</summary>
public sealed class ListUsersQueryHandler : IQueryHandler<ListUsersQuery, IReadOnlyList<UserSummary>>
{
    private readonly IUserRepository _users;

    /// <summary>Initializes the handler.</summary>
    public ListUsersQueryHandler(IUserRepository users)
    {
        _users = users;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<UserSummary>>> HandleAsync(
        ListUsersQuery request,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<User> users = await _users.ListAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success<IReadOnlyList<UserSummary>>(
            [.. users.Select(user => new UserSummary(
                user.Id.Value,
                user.Email,
                user.DisplayName,
                user.IsActive,
                user.MustChangePassword,
                user.LockedUntilUtc,
                user.LastSignedInAtUtc,
                [.. user.Roles.Select(role => role.RoleId.Value)]))]);
    }
}

/// <summary>A role, as an administration screen shows it.</summary>
/// <param name="RoleId">The role.</param>
/// <param name="Code">Its short code.</param>
/// <param name="Name">Its display name.</param>
/// <param name="Description">What it is for.</param>
/// <param name="IsSystem">True for the administrator role, which cannot be deleted.</param>
/// <param name="Permissions">What holders may do.</param>
public sealed record RoleSummary(
    Guid RoleId,
    string Code,
    string Name,
    string? Description,
    bool IsSystem,
    IReadOnlyList<string> Permissions);

/// <summary>
/// A user, as an administration screen shows them.
/// <para>
/// No password hash, obviously, and no permissions either — those come from the roles, and
/// showing a flattened copy here would be a second answer to "what can this person do" that
/// nothing keeps in step with the first.
/// </para>
/// </summary>
/// <param name="UserId">The user.</param>
/// <param name="Email">How they sign in.</param>
/// <param name="DisplayName">Their name.</param>
/// <param name="IsActive">False once the account has been closed.</param>
/// <param name="MustChangePassword">True while they are still using a password somebody gave them.</param>
/// <param name="LockedUntilUtc">When a lockout lifts, while one is in force.</param>
/// <param name="LastSignedInAtUtc">When they were last in.</param>
/// <param name="RoleIds">The roles they hold.</param>
public sealed record UserSummary(
    Guid UserId,
    string Email,
    string DisplayName,
    bool IsActive,
    bool MustChangePassword,
    DateTimeOffset? LockedUntilUtc,
    DateTimeOffset? LastSignedInAtUtc,
    IReadOnlyList<Guid> RoleIds);
