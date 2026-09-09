using AutoPartsErp.Modules.Abstractions.Http;
using AutoPartsErp.Modules.Abstractions.Modules;
using AutoPartsErp.Modules.Access.Application.Administration;
using AutoPartsErp.Modules.Access.Application.Authentication;
using AutoPartsErp.SharedKernel.Authorization;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AutoPartsErp.Modules.Access.Presentation.Endpoints;

/// <summary>
/// Getting in, staying in, and getting out.
/// <para>
/// The only routes in this system that are open to somebody holding no token, and they are open
/// because they have to be: sign-in is where a token comes from. Everything else in every module
/// is closed by default.
/// </para>
/// </summary>
public sealed class AuthenticationEndpoints : IEndpointGroup
{
    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapPost("/sign-in", SignInAsync)
            .AllowAnonymous()
            .WithName("SignIn")
            .WithSummary("Exchange an email and password for a token.")
            .Produces<SignInResult>()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/refresh", RefreshAsync)
            .AllowAnonymous()
            .WithName("RefreshSession")
            .WithSummary("Exchange a refresh handle for a new token. The handle is replaced.")
            .Produces<SignInResult>()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/sign-out", SignOutAsync)
            .AllowAnonymous()
            .WithName("SignOut")
            .WithSummary("End the session a refresh handle belongs to.")
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/me", MeAsync)
            .WithName("GetMyAccess")
            .WithSummary("Who the caller is and what their token says they may do.")
            .Produces<CurrentAccessResponse>();

        group.MapPost("/me/password", ChangeMyPasswordAsync)
            .WithName("ChangeMyPassword")
            .WithSummary("Change your own password. Ends every other session you have.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> SignInAsync(
        IDispatcher dispatcher,
        SignInCommand command,
        CancellationToken cancellationToken)
    {
        Result<SignInResult> result = await dispatcher.SendAsync(command, cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> RefreshAsync(
        IDispatcher dispatcher,
        RefreshSessionCommand command,
        CancellationToken cancellationToken)
    {
        Result<SignInResult> result = await dispatcher.SendAsync(command, cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> SignOutAsync(
        IDispatcher dispatcher,
        SignOutCommand command,
        CancellationToken cancellationToken)
    {
        Result result = await dispatcher.SendAsync(command, cancellationToken);

        return result.ToNoContent();
    }

    /// <summary>
    /// Reads the caller's own token back to them.
    /// <para>
    /// Straight off the claims rather than out of the database, and that is the point: it answers
    /// what this token can do, which is what a client needs in order to hide the buttons that
    /// would be refused. Reading the database instead would show permissions the token does not
    /// carry yet, and the buttons would appear and then fail.
    /// </para>
    /// </summary>
    private static IResult MeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Results.Ok(new CurrentAccessResponse(
            context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? string.Empty,
            context.User.Identity?.Name ?? string.Empty,
            context.User.FindFirst(ErpClaims.TenantId)?.Value ?? string.Empty,
            [.. context.User.FindAll(ErpClaims.Permission).Select(claim => claim.Value).Order(StringComparer.Ordinal)]));
    }

    private static async Task<IResult> ChangeMyPasswordAsync(
        IDispatcher dispatcher,
        ChangeMyPasswordCommand command,
        CancellationToken cancellationToken)
    {
        Result result = await dispatcher.SendAsync(command, cancellationToken);

        return result.ToNoContent();
    }
}

/// <summary>Who the caller is, according to the token they presented.</summary>
/// <param name="UserId">Their identifier.</param>
/// <param name="DisplayName">Their name.</param>
/// <param name="TenantId">The company the token was issued for.</param>
/// <param name="Permissions">What the token says they may do.</param>
public sealed record CurrentAccessResponse(
    string UserId,
    string DisplayName,
    string TenantId,
    IReadOnlyList<string> Permissions);

/// <summary>Managing the people who may use the system.</summary>
public sealed class UserEndpoints : IEndpointGroup
{
    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        RouteGroupBuilder users = group.MapGroup("/users");

        users.MapGet("/", ListAsync)
            .RequirePermission(Permissions.Access.Read)
            .WithName("ListUsers")
            .WithSummary("Everybody with an account here.")
            .Produces<IReadOnlyList<UserSummary>>();

        users.MapPost("/", CreateAsync)
            .RequirePermission(Permissions.Access.ManageUsers)
            .WithName("CreateUser")
            .WithSummary("Open an account. The password must be changed on first sign-in.")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        users.MapPut("/{userId:guid}/roles", SetRolesAsync)
            .RequirePermission(Permissions.Access.ManageRoles)
            .WithName("SetUserRoles")
            .WithSummary("Replace the roles somebody holds.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        users.MapPost("/{userId:guid}/password", ResetPasswordAsync)
            .RequirePermission(Permissions.Access.ManageUsers)
            .WithName("ResetUserPassword")
            .WithSummary("Set somebody's password. Ends every session they have.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();

        users.MapPut("/{userId:guid}/active", SetActiveAsync)
            .RequirePermission(Permissions.Access.ManageUsers)
            .WithName("SetUserActive")
            .WithSummary("Close or reopen an account. Accounts are never deleted.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        users.MapPost("/{userId:guid}/unlock", UnlockAsync)
            .RequirePermission(Permissions.Access.ManageUsers)
            .WithName("UnlockUser")
            .WithSummary("Lift a lockout without waiting for it to expire.")
            .Produces(StatusCodes.Status204NoContent);
    }

    private static async Task<IResult> ListAsync(
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<UserSummary>> result =
            await dispatcher.SendAsync(new ListUsersQuery(), cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> CreateAsync(
        IDispatcher dispatcher,
        CreateUserCommand command,
        CancellationToken cancellationToken)
    {
        Result<Guid> result = await dispatcher.SendAsync(command, cancellationToken);

        return result.ToCreated(id => $"/api/access/users/{id}");
    }

    private static async Task<IResult> SetRolesAsync(
        IDispatcher dispatcher,
        Guid userId,
        SetRolesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result result = await dispatcher.SendAsync(
            new SetUserRolesCommand(userId, request.RoleIds), cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> ResetPasswordAsync(
        IDispatcher dispatcher,
        Guid userId,
        ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result result = await dispatcher.SendAsync(
            new ResetPasswordCommand(userId, request.NewPassword), cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> SetActiveAsync(
        IDispatcher dispatcher,
        Guid userId,
        SetActiveRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result result = await dispatcher.SendAsync(
            new SetUserActiveCommand(userId, request.IsActive), cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> UnlockAsync(
        IDispatcher dispatcher,
        Guid userId,
        CancellationToken cancellationToken)
    {
        Result result = await dispatcher.SendAsync(new UnlockUserCommand(userId), cancellationToken);

        return result.ToNoContent();
    }
}

/// <summary>The roles somebody should hold.</summary>
/// <param name="RoleIds">The complete set. An empty list means they can do nothing.</param>
public sealed record SetRolesRequest(IReadOnlyList<Guid> RoleIds);

/// <summary>A password set on somebody's behalf.</summary>
/// <param name="NewPassword">At least twelve characters. They will have to change it.</param>
public sealed record ResetPasswordRequest(string NewPassword);

/// <summary>Whether an account is open.</summary>
/// <param name="IsActive">True to reopen, false to close.</param>
public sealed record SetActiveRequest(bool IsActive);

/// <summary>Deciding what the jobs are and what they involve.</summary>
public sealed class RoleEndpoints : IEndpointGroup
{
    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        RouteGroupBuilder roles = group.MapGroup("/roles");

        roles.MapGet("/", ListAsync)
            .RequirePermission(Permissions.Access.Read)
            .WithName("ListRoles")
            .WithSummary("The roles in this company and what each may do.")
            .Produces<IReadOnlyList<RoleSummary>>();

        roles.MapGet("/permissions", ListPermissionsAsync)
            .RequirePermission(Permissions.Access.Read)
            .WithName("ListPermissions")
            .WithSummary("Every permission this system has, for a screen that builds a role.")
            .Produces<IReadOnlyList<string>>();

        roles.MapPost("/", CreateAsync)
            .RequirePermission(Permissions.Access.ManageRoles)
            .WithName("CreateRole")
            .WithSummary("Create a role.")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        roles.MapPut("/{roleId:guid}", RenameAsync)
            .RequirePermission(Permissions.Access.ManageRoles)
            .WithName("RenameRole")
            .WithSummary("Rename a role. Its code never changes.")
            .Produces(StatusCodes.Status204NoContent);

        roles.MapPut("/{roleId:guid}/permissions", SetPermissionsAsync)
            .RequirePermission(Permissions.Access.ManageRoles)
            .WithName("SetRolePermissions")
            .WithSummary("Replace what a role may do. Takes effect as tokens expire.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();

        roles.MapDelete("/{roleId:guid}", DeleteAsync)
            .RequirePermission(Permissions.Access.ManageRoles)
            .WithName("DeleteRole")
            .WithSummary("Delete a role nobody holds.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> ListAsync(
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<RoleSummary>> result =
            await dispatcher.SendAsync(new ListRolesQuery(), cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> ListPermissionsAsync(
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<string>> result =
            await dispatcher.SendAsync(new ListPermissionsQuery(), cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> CreateAsync(
        IDispatcher dispatcher,
        CreateRoleCommand command,
        CancellationToken cancellationToken)
    {
        Result<Guid> result = await dispatcher.SendAsync(command, cancellationToken);

        return result.ToCreated(id => $"/api/access/roles/{id}");
    }

    private static async Task<IResult> RenameAsync(
        IDispatcher dispatcher,
        Guid roleId,
        RenameRoleRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result result = await dispatcher.SendAsync(
            new RenameRoleCommand(roleId, request.Name, request.Description), cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> SetPermissionsAsync(
        IDispatcher dispatcher,
        Guid roleId,
        SetPermissionsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result result = await dispatcher.SendAsync(
            new SetRolePermissionsCommand(roleId, request.Permissions), cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> DeleteAsync(
        IDispatcher dispatcher,
        Guid roleId,
        CancellationToken cancellationToken)
    {
        Result result = await dispatcher.SendAsync(new DeleteRoleCommand(roleId), cancellationToken);

        return result.ToNoContent();
    }
}

/// <summary>A role's new name.</summary>
/// <param name="Name">What it should be called.</param>
/// <param name="Description">What the role is for.</param>
public sealed record RenameRoleRequest(string Name, string? Description);

/// <summary>What a role may do.</summary>
/// <param name="Permissions">The complete set it should carry.</param>
public sealed record SetPermissionsRequest(IReadOnlyList<string> Permissions);
