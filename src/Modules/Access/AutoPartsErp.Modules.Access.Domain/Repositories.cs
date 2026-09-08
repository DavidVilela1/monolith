using AutoPartsErp.Modules.Access.Domain.Roles;
using AutoPartsErp.Modules.Access.Domain.Sessions;
using AutoPartsErp.Modules.Access.Domain.Users;
using AutoPartsErp.SharedKernel.Abstractions;

namespace AutoPartsErp.Modules.Access.Domain;

/// <summary>Write-side access to users.</summary>
public interface IUserRepository : IRepository<User, UserId>
{
    /// <summary>Finds a user by the address they sign in with, or null.</summary>
    Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>True when that address already has an account in this tenant.</summary>
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Every active user holding any of these roles.</summary>
    Task<IReadOnlyList<User>> GetActiveHoldersAsync(
        IReadOnlyCollection<RoleId> roles,
        CancellationToken cancellationToken = default);

    /// <summary>Every user, for the administration screen.</summary>
    Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken = default);
}

/// <summary>Write-side access to roles.</summary>
public interface IRoleRepository : IRepository<Role, RoleId>
{
    /// <summary>Finds a role by its code, or null.</summary>
    Task<Role?> FindByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Loads several roles at once, for resolving a user's permissions.</summary>
    Task<IReadOnlyList<Role>> GetManyAsync(
        IReadOnlyCollection<RoleId> ids,
        CancellationToken cancellationToken = default);

    /// <summary>Every role in the tenant.</summary>
    Task<IReadOnlyList<Role>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>How many users hold this role.</summary>
    Task<int> CountHoldersAsync(RoleId roleId, CancellationToken cancellationToken = default);
}

/// <summary>Storage for the handles that keep a session alive.</summary>
public interface IRefreshTokenRepository
{
    /// <summary>Finds a stored handle by its hash, or null.</summary>
    Task<RefreshToken?> FindAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>Stages a new handle.</summary>
    void Add(RefreshToken token);

    /// <summary>
    /// Withdraws every live handle for a user.
    /// <para>
    /// Used when a password changes, when an account is closed, and when a handle is presented
    /// twice — the last being the case that matters, because a handle used twice is either stolen
    /// or a bug, and both are best answered by ending every session that user has.
    /// </para>
    /// </summary>
    Task RevokeAllForUserAsync(
        UserId userId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes handles that expired long enough ago to be of no interest.</summary>
    Task<int> PurgeExpiredAsync(DateTimeOffset before, CancellationToken cancellationToken = default);
}

/// <summary>The Access module's unit of work.</summary>
public interface IAccessUnitOfWork : IUnitOfWork;
