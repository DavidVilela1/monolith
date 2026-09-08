using AutoPartsErp.Modules.Access.Domain;
using AutoPartsErp.Modules.Access.Domain.Roles;
using AutoPartsErp.Modules.Access.Domain.Sessions;
using AutoPartsErp.Modules.Access.Domain.Users;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Access.Application.Authentication;

/// <summary>Exchanges an email and a password for a token.</summary>
/// <param name="Email">The address they sign in with.</param>
/// <param name="Password">What they typed.</param>
public sealed record SignInCommand(string Email, string Password) : ICommand<SignInResult>;

/// <summary>
/// What a successful sign-in hands back.
/// </summary>
/// <param name="AccessToken">The signed token every other call carries.</param>
/// <param name="ExpiresAtUtc">When it stops being accepted.</param>
/// <param name="RefreshToken">
/// The handle that buys a new access token without a password. Store it as carefully as the
/// password: for as long as it lives, it is one.
/// </param>
/// <param name="UserId">Who signed in.</param>
/// <param name="DisplayName">Their name.</param>
/// <param name="Permissions">What they may do, resolved from their roles.</param>
/// <param name="MustChangePassword">
/// True when an administrator set this password. The client should send them to a change-password
/// screen and nowhere else.
/// </param>
public sealed record SignInResult(
    string AccessToken,
    DateTimeOffset ExpiresAtUtc,
    string RefreshToken,
    Guid UserId,
    string DisplayName,
    IReadOnlyCollection<string> Permissions,
    bool MustChangePassword);

/// <summary>
/// Checks the password and issues a token.
/// <para>
/// Three things here are deliberate and each of them is the kind of detail that gets left out.
/// </para>
/// <para>
/// <b>One error for everything.</b> No account, wrong password, closed account — the caller is
/// told the same thing. Telling them apart turns this endpoint into a way of discovering who has
/// an account here, which is the first thing anybody attacking it wants to know. The lockout
/// message is the one exception, because a person who is locked out cannot act on advice they are
/// not given, and an attacker learns nothing from it that five failures did not already tell them.
/// </para>
/// <para>
/// <b>The hash is verified even when there is no account.</b> Otherwise the endpoint answers
/// faster for unknown addresses than for known ones, and the difference is measurable from the
/// far side of the internet — the same disclosure the shared error message just prevented.
/// </para>
/// <para>
/// <b>Failures are saved.</b> The lockout counter is worthless if the wrong-password path returns
/// before writing it down.
/// </para>
/// </summary>
public sealed class SignInCommandHandler : ICommandHandler<SignInCommand, SignInResult>
{
    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IPasswordHasher _passwords;
    private readonly IAccessTokenIssuer _tokens;
    private readonly IAccessUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public SignInCommandHandler(
        IUserRepository users,
        IRoleRepository roles,
        IRefreshTokenRepository refreshTokens,
        IPasswordHasher passwords,
        IAccessTokenIssuer tokens,
        IAccessUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _users = users;
        _roles = roles;
        _refreshTokens = refreshTokens;
        _passwords = passwords;
        _tokens = tokens;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result<SignInResult>> HandleAsync(
        SignInCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        DateTimeOffset now = _clock.UtcNow;

        User? user = await _users
            .FindByEmailAsync(request.Email ?? string.Empty, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            // Burn the same time a real verification would take. Without this the endpoint
            // answers unknown addresses noticeably faster than known ones, and the shared error
            // message above stops hiding anything.
            _passwords.VerifyDummy(request.Password ?? string.Empty);

            return Result.Failure<SignInResult>(AccessErrors.User.InvalidCredentials);
        }

        if (user.IsLockedOut(now))
        {
            return Result.Failure<SignInResult>(AccessErrors.User.LockedOut(user.LockedUntilUtc!.Value));
        }

        if (!_passwords.Verify(user.PasswordHash, request.Password ?? string.Empty))
        {
            user.SignInFailed(now);
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return Result.Failure<SignInResult>(AccessErrors.User.InvalidCredentials);
        }

        // Only after the password is right. A closed account that reported itself as closed
        // before the password was checked would tell anybody who asked that the address exists.
        Result signedIn = user.SignedIn(now);

        if (signedIn.IsFailure)
        {
            return Result.Failure<SignInResult>(AccessErrors.User.InvalidCredentials);
        }

        IReadOnlyCollection<string> permissions = await ResolvePermissionsAsync(
            _roles, user, cancellationToken).ConfigureAwait(false);

        IssuedAccessToken access = _tokens.Issue(user, permissions, now);
        IssuedRefreshToken refresh = _tokens.IssueRefreshToken();

        _refreshTokens.Add(RefreshToken.Issue(
            user.Id, refresh.Hash, now, _tokens.RefreshTokenLifetime));

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new SignInResult(
            access.Token,
            access.ExpiresAtUtc,
            refresh.Token,
            user.Id.Value,
            user.DisplayName,
            permissions,
            user.MustChangePassword);
    }

    /// <summary>
    /// Flattens a user's roles into the set of permissions that goes into their token.
    /// <para>
    /// Resolved at sign-in and frozen into the token, which has a consequence worth naming: a
    /// permission taken away from a role does not reach somebody already signed in until their
    /// access token expires. That window is the access token's lifetime and nothing longer, which
    /// is the whole reason the lifetime is short.
    /// </para>
    /// </summary>
    internal static async Task<IReadOnlyCollection<string>> ResolvePermissionsAsync(
        IRoleRepository roles,
        User user,
        CancellationToken cancellationToken)
    {
        RoleId[] held = [.. user.Roles.Select(role => role.RoleId)];

        if (held.Length == 0)
        {
            return [];
        }

        IReadOnlyList<Role> loaded = await roles
            .GetManyAsync(held, cancellationToken)
            .ConfigureAwait(false);

        return [.. loaded
            .SelectMany(role => role.PermissionNames)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }
}

/// <summary>Exchanges a refresh handle for a new access token.</summary>
/// <param name="RefreshToken">The handle the client was given.</param>
public sealed record RefreshSessionCommand(string RefreshToken) : ICommand<SignInResult>;

/// <summary>
/// Rotates the session.
/// <para>
/// The used handle is withdrawn and a new one issued, so each handle works exactly once. A handle
/// presented a second time is answered by withdrawing every session that user has: it means
/// either a copy somebody else is holding or a client bug, and there is no version of either
/// where quietly issuing another token is the right answer.
/// </para>
/// </summary>
public sealed class RefreshSessionCommandHandler : ICommandHandler<RefreshSessionCommand, SignInResult>
{
    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IAccessTokenIssuer _tokens;
    private readonly IAccessUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public RefreshSessionCommandHandler(
        IUserRepository users,
        IRoleRepository roles,
        IRefreshTokenRepository refreshTokens,
        IAccessTokenIssuer tokens,
        IAccessUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _users = users;
        _roles = roles;
        _refreshTokens = refreshTokens;
        _tokens = tokens;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result<SignInResult>> HandleAsync(
        RefreshSessionCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        DateTimeOffset now = _clock.UtcNow;
        string hash = _tokens.HashRefreshToken(request.RefreshToken ?? string.Empty);

        RefreshToken? stored = await _refreshTokens
            .FindAsync(hash, cancellationToken)
            .ConfigureAwait(false);

        if (stored is null)
        {
            return Result.Failure<SignInResult>(AccessErrors.Token.RefreshRejected);
        }

        if (!stored.IsUsable(now))
        {
            // Already used or already withdrawn. If it was used, somebody is holding a copy of a
            // handle that has been spent - end every session this user has and make them sign in.
            if (stored.RevokedAtUtc is not null)
            {
                await _refreshTokens
                    .RevokeAllForUserAsync(stored.UserId, now, cancellationToken)
                    .ConfigureAwait(false);

                await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            return Result.Failure<SignInResult>(AccessErrors.Token.RefreshRejected);
        }

        User? user = await _users
            .GetByIdAsync(stored.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (user is null || !user.IsActive || user.IsLockedOut(now))
        {
            stored.Revoke(now);
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return Result.Failure<SignInResult>(AccessErrors.Token.RefreshRejected);
        }

        stored.Revoke(now);

        IReadOnlyCollection<string> permissions = await SignInCommandHandler
            .ResolvePermissionsAsync(_roles, user, cancellationToken)
            .ConfigureAwait(false);

        IssuedAccessToken access = _tokens.Issue(user, permissions, now);
        IssuedRefreshToken refresh = _tokens.IssueRefreshToken();

        _refreshTokens.Add(RefreshToken.Issue(
            user.Id, refresh.Hash, now, _tokens.RefreshTokenLifetime));

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new SignInResult(
            access.Token,
            access.ExpiresAtUtc,
            refresh.Token,
            user.Id.Value,
            user.DisplayName,
            permissions,
            user.MustChangePassword);
    }
}

/// <summary>Ends the session the handle belongs to.</summary>
/// <param name="RefreshToken">The handle to withdraw.</param>
public sealed record SignOutCommand(string RefreshToken) : ICommand;

/// <summary>
/// Withdraws the handle.
/// <para>
/// The access token stays valid until it expires, and nothing here can change that — it is a
/// signed statement already in somebody's hands. What signing out does is stop the session being
/// renewed, which bounds the damage to the access token's remaining minutes.
/// </para>
/// </summary>
public sealed class SignOutCommandHandler : ICommandHandler<SignOutCommand>
{
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IAccessTokenIssuer _tokens;
    private readonly IAccessUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public SignOutCommandHandler(
        IRefreshTokenRepository refreshTokens,
        IAccessTokenIssuer tokens,
        IAccessUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _refreshTokens = refreshTokens;
        _tokens = tokens;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Succeeds whether or not the handle was recognized. Signing out is not a place to report
    /// that a token was unknown: the caller wanted the session gone, and it is.
    /// </remarks>
    public async Task<Result> HandleAsync(
        SignOutCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string hash = _tokens.HashRefreshToken(request.RefreshToken ?? string.Empty);

        RefreshToken? stored = await _refreshTokens
            .FindAsync(hash, cancellationToken)
            .ConfigureAwait(false);

        if (stored is not null)
        {
            stored.Revoke(_clock.UtcNow);
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return Result.Success();
    }
}
