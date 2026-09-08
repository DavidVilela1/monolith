using AutoPartsErp.Modules.Access.Domain;
using AutoPartsErp.Modules.Access.Domain.Roles;
using AutoPartsErp.Modules.Access.Domain.Users;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Access.Application.Administration;

/// <summary>Rules about passwords that apply wherever one is set.</summary>
/// <remarks>
/// One rule, and only one on purpose: length. Composition rules — a capital, a digit, a symbol —
/// measurably push people towards <c>Password1!</c> and towards writing it on the monitor, and
/// they do not survive contact with a machine guessing at scale. Twelve characters of anything
/// does.
/// </remarks>
internal static class PasswordRules
{
    public const int MinimumLength = 12;

    public static Result Check(string? password) =>
        string.IsNullOrWhiteSpace(password)
            ? AccessErrors.User.PasswordRequired
            : password.Length >= MinimumLength
                ? Result.Success()
                : AccessErrors.User.PasswordTooShort;
}

/// <summary>Opens an account.</summary>
/// <param name="Email">The address they will sign in with.</param>
/// <param name="DisplayName">Their name, which will appear on documents they issue.</param>
/// <param name="Password">The password they are given. They will have to change it.</param>
/// <param name="RoleIds">The roles they hold. May be empty; they will be able to do nothing.</param>
public sealed record CreateUserCommand(
    string Email,
    string DisplayName,
    string Password,
    IReadOnlyList<Guid> RoleIds) : ICommand<Guid>;

/// <summary>Creates the user, after checking the address is free and the roles exist.</summary>
public sealed class CreateUserCommandHandler : ICommandHandler<CreateUserCommand, Guid>
{
    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly IPasswordHasher _passwords;
    private readonly IAccessUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public CreateUserCommandHandler(
        IUserRepository users,
        IRoleRepository roles,
        IPasswordHasher passwords,
        IAccessUnitOfWork unitOfWork)
    {
        _users = users;
        _roles = roles;
        _passwords = passwords;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> HandleAsync(
        CreateUserCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result password = PasswordRules.Check(request.Password);

        if (password.IsFailure)
        {
            return Result.Failure<Guid>(password.Error);
        }

        string email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();

        if (await _users.EmailExistsAsync(email, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<Guid>(AccessErrors.User.EmailAlreadyExists(email));
        }

        Result<IReadOnlyList<RoleId>> roles = await ResolveRolesAsync(
            _roles, request.RoleIds, cancellationToken).ConfigureAwait(false);

        if (roles.IsFailure)
        {
            return Result.Failure<Guid>(roles.Error);
        }

        Result<User> user = User.Create(
            request.Email ?? string.Empty,
            request.DisplayName ?? string.Empty,
            _passwords.Hash(request.Password));

        if (user.IsFailure)
        {
            return Result.Failure<Guid>(user.Error);
        }

        user.Value.SetRoles(roles.Value);

        _users.Add(user.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return user.Value.Id.Value;
    }

    /// <summary>Turns requested role ids into real ones, refusing any that do not exist.</summary>
    /// <remarks>
    /// Refusing rather than skipping. A silently dropped role means somebody was created without
    /// the access they were meant to have, and the first anybody hears of it is a person who
    /// cannot do their job.
    /// </remarks>
    internal static async Task<Result<IReadOnlyList<RoleId>>> ResolveRolesAsync(
        IRoleRepository roles,
        IReadOnlyList<Guid>? requested,
        CancellationToken cancellationToken)
    {
        if (requested is null || requested.Count == 0)
        {
            return Result.Success<IReadOnlyList<RoleId>>([]);
        }

        RoleId[] ids = [.. requested.Distinct().Select(id => new RoleId(id))];

        IReadOnlyList<Role> found = await roles
            .GetManyAsync(ids, cancellationToken)
            .ConfigureAwait(false);

        foreach (RoleId id in ids)
        {
            if (!found.Any(role => role.Id == id))
            {
                return Result.Failure<IReadOnlyList<RoleId>>(AccessErrors.Role.NotFound(id.ToString()));
            }
        }

        return Result.Success<IReadOnlyList<RoleId>>(ids);
    }
}

/// <summary>Changes a person's own password.</summary>
/// <param name="CurrentPassword">What it is now. Required even for a password they must change.</param>
/// <param name="NewPassword">What it should become.</param>
public sealed record ChangeMyPasswordCommand(
    string CurrentPassword,
    string NewPassword) : ICommand;

/// <summary>
/// Replaces the caller's password and ends every other session they have.
/// <para>
/// Ending the other sessions is the point of changing a password. Somebody who changes theirs
/// because they think it was seen, and finds the intruder still signed in, has been given a
/// button that does nothing.
/// </para>
/// </summary>
public sealed class ChangeMyPasswordCommandHandler : ICommandHandler<ChangeMyPasswordCommand>
{
    private readonly IUserRepository _users;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IPasswordHasher _passwords;
    private readonly IAccessUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public ChangeMyPasswordCommandHandler(
        IUserRepository users,
        IRefreshTokenRepository refreshTokens,
        IPasswordHasher passwords,
        IAccessUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _users = users;
        _refreshTokens = refreshTokens;
        _passwords = passwords;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        ChangeMyPasswordCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Guid.TryParse(_currentUser.UserId, out Guid id))
        {
            return AccessErrors.User.NotFound(_currentUser.UserId);
        }

        User? user = await _users
            .GetByIdAsync(new UserId(id), cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            return AccessErrors.User.NotFound(_currentUser.UserId);
        }

        if (!_passwords.Verify(user.PasswordHash, request.CurrentPassword ?? string.Empty))
        {
            return AccessErrors.User.CurrentPasswordWrong;
        }

        Result rules = PasswordRules.Check(request.NewPassword);

        if (rules.IsFailure)
        {
            return rules;
        }

        Result changed = user.ChangePassword(_passwords.Hash(request.NewPassword));

        if (changed.IsFailure)
        {
            return changed;
        }

        await _refreshTokens
            .RevokeAllForUserAsync(user.Id, _clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Sets somebody else's password, for when they have forgotten it.</summary>
/// <param name="UserId">Whose.</param>
/// <param name="NewPassword">What to set it to. They will have to change it.</param>
public sealed record ResetPasswordCommand(Guid UserId, string NewPassword) : ICommand;

/// <summary>Replaces the roles a user holds.</summary>
/// <param name="UserId">Whose.</param>
/// <param name="RoleIds">The complete set they should hold.</param>
public sealed record SetUserRolesCommand(Guid UserId, IReadOnlyList<Guid> RoleIds) : ICommand;

/// <summary>Closes or reopens an account, or lifts a lockout.</summary>
/// <param name="UserId">Whose.</param>
/// <param name="IsActive">True to reopen, false to close.</param>
public sealed record SetUserActiveCommand(Guid UserId, bool IsActive) : ICommand;

/// <summary>Lifts a lockout, for when somebody rings up rather than waiting fifteen minutes.</summary>
/// <param name="UserId">Whose.</param>
public sealed record UnlockUserCommand(Guid UserId) : ICommand;

/// <summary>
/// The administration commands that all load one user and change it.
/// <para>
/// One handler for four commands because the interesting logic is not in any of them — it is in
/// the check they share: this company must never lose its last administrator. Spread across four
/// classes that rule gets remembered in three of them.
/// </para>
/// </summary>
public sealed class UserAdministrationCommandHandler
    : ICommandHandler<ResetPasswordCommand>,
      ICommandHandler<SetUserRolesCommand>,
      ICommandHandler<SetUserActiveCommand>,
      ICommandHandler<UnlockUserCommand>
{
    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IPasswordHasher _passwords;
    private readonly IAccessUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public UserAdministrationCommandHandler(
        IUserRepository users,
        IRoleRepository roles,
        IRefreshTokenRepository refreshTokens,
        IPasswordHasher passwords,
        IAccessUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _users = users;
        _roles = roles;
        _refreshTokens = refreshTokens;
        _passwords = passwords;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        ResetPasswordCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result rules = PasswordRules.Check(request.NewPassword);

        if (rules.IsFailure)
        {
            return rules;
        }

        User? user = await LoadAsync(request.UserId, cancellationToken).ConfigureAwait(false);

        if (user is null)
        {
            return AccessErrors.User.NotFound(request.UserId.ToString());
        }

        Result reset = user.ResetPassword(_passwords.Hash(request.NewPassword));

        if (reset.IsFailure)
        {
            return reset;
        }

        // Every session ends. A reset is what happens after "I think somebody has my password",
        // and leaving their sessions running would make it ceremony.
        await _refreshTokens
            .RevokeAllForUserAsync(user.Id, _clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        SetUserRolesCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        User? user = await LoadAsync(request.UserId, cancellationToken).ConfigureAwait(false);

        if (user is null)
        {
            return AccessErrors.User.NotFound(request.UserId.ToString());
        }

        Result<IReadOnlyList<RoleId>> resolved = await CreateUserCommandHandler
            .ResolveRolesAsync(_roles, request.RoleIds, cancellationToken)
            .ConfigureAwait(false);

        if (resolved.IsFailure)
        {
            return Result.FromError(resolved.Error);
        }

        Result lastAdmin = await WouldStrandTheCompanyAsync(
            user, resolved.Value, stillActive: true, cancellationToken).ConfigureAwait(false);

        if (lastAdmin.IsFailure)
        {
            return lastAdmin;
        }

        user.SetRoles(resolved.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        SetUserActiveCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        User? user = await LoadAsync(request.UserId, cancellationToken).ConfigureAwait(false);

        if (user is null)
        {
            return AccessErrors.User.NotFound(request.UserId.ToString());
        }

        if (request.IsActive)
        {
            user.Reactivate();
        }
        else
        {
            Result lastAdmin = await WouldStrandTheCompanyAsync(
                user,
                [.. user.Roles.Select(role => role.RoleId)],
                stillActive: false,
                cancellationToken).ConfigureAwait(false);

            if (lastAdmin.IsFailure)
            {
                return lastAdmin;
            }

            user.Deactivate();

            await _refreshTokens
                .RevokeAllForUserAsync(user.Id, _clock.UtcNow, cancellationToken)
                .ConfigureAwait(false);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        UnlockUserCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        User? user = await LoadAsync(request.UserId, cancellationToken).ConfigureAwait(false);

        if (user is null)
        {
            return AccessErrors.User.NotFound(request.UserId.ToString());
        }

        user.Unlock();
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    private Task<User?> LoadAsync(Guid userId, CancellationToken cancellationToken) =>
        _users.GetByIdAsync(new UserId(userId), cancellationToken);

    /// <summary>
    /// Refuses a change that would leave nobody able to manage roles.
    /// <para>
    /// The one irreversible mistake this module can make. A company that removes the last account
    /// holding <c>access.role.manage</c> cannot grant it back — the screen that would do it is
    /// behind the permission nobody has — and the only remedy is somebody with a database client.
    /// It is worth a query on every role change to never have that conversation.
    /// </para>
    /// </summary>
    private async Task<Result> WouldStrandTheCompanyAsync(
        User user,
        IReadOnlyList<RoleId> intendedRoles,
        bool stillActive,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Role> allRoles = await _roles.ListAsync(cancellationToken).ConfigureAwait(false);

        RoleId[] administering = [.. allRoles
            .Where(role => role.Grants(Permissions.Access.ManageRoles))
            .Select(role => role.Id)];

        if (administering.Length == 0)
        {
            return Result.Success();
        }

        bool wouldStillAdminister = stillActive
            && intendedRoles.Any(role => administering.Contains(role));

        if (wouldStillAdminister)
        {
            return Result.Success();
        }

        IReadOnlyList<User> holders = await _users
            .GetActiveHoldersAsync(administering, cancellationToken)
            .ConfigureAwait(false);

        bool somebodyElseCan = holders.Any(holder => holder.Id != user.Id);

        return somebodyElseCan ? Result.Success() : AccessErrors.User.LastAdministrator;
    }
}
