using AutoPartsErp.Modules.Access.Domain;
using AutoPartsErp.Modules.Access.Domain.Roles;
using AutoPartsErp.Modules.Access.Domain.Sessions;
using AutoPartsErp.Modules.Access.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsErp.Modules.Access.Infrastructure.Persistence.Repositories;

/// <summary>Write-side access to users.</summary>
public sealed class UserRepository : IUserRepository
{
    private readonly AccessDbContext _context;

    /// <summary>Initializes the repository.</summary>
    public UserRepository(AccessDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public Task<User?> GetByIdAsync(UserId id, CancellationToken cancellationToken = default) =>
        _context.Users.FirstOrDefaultAsync(user => user.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(UserId id, CancellationToken cancellationToken = default) =>
        _context.Users.AnyAsync(user => user.Id == id, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Normalized the same way the aggregate normalizes on the way in. A lookup that only matched
    /// the exact casing somebody typed would let one address have two accounts, and would tell
    /// the second person their password was wrong.
    /// </remarks>
    public Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        string normalized = (email ?? string.Empty).Trim().ToLowerInvariant();

        return _context.Users.FirstOrDefaultAsync(
            user => user.Email == normalized, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default)
    {
        string normalized = (email ?? string.Empty).Trim().ToLowerInvariant();

        return _context.Users.AnyAsync(user => user.Email == normalized, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<User>> GetActiveHoldersAsync(
        IReadOnlyCollection<RoleId> roles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roles);

        if (roles.Count == 0)
        {
            return [];
        }

        List<User> users = await _context.Users
            .Where(user => user.IsActive)
            .Where(user => user.Roles.Any(role => roles.Contains(role.RoleId)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return users;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken = default)
    {
        List<User> users = await _context.Users
            .OrderBy(user => user.Email)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return users;
    }

    /// <inheritdoc />
    public void Add(User aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.Users.Add(aggregate);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Present because the interface has it, and it should not be called. A user is deactivated,
    /// never removed: their name is on documents that have to stay explicable for ten years.
    /// </remarks>
    public void Remove(User aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.Users.Remove(aggregate);
    }
}

/// <summary>Write-side access to roles.</summary>
public sealed class RoleRepository : IRoleRepository
{
    private readonly AccessDbContext _context;

    /// <summary>Initializes the repository.</summary>
    public RoleRepository(AccessDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public Task<Role?> GetByIdAsync(RoleId id, CancellationToken cancellationToken = default) =>
        _context.Roles.FirstOrDefaultAsync(role => role.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(RoleId id, CancellationToken cancellationToken = default) =>
        _context.Roles.AnyAsync(role => role.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<Role?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        string normalized = (code ?? string.Empty).Trim().ToUpperInvariant();

        return _context.Roles.FirstOrDefaultAsync(role => role.Code == normalized, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Role>> GetManyAsync(
        IReadOnlyCollection<RoleId> ids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
        {
            return [];
        }

        List<Role> roles = await _context.Roles
            .Where(role => ids.Contains(role.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return roles;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Role>> ListAsync(CancellationToken cancellationToken = default)
    {
        List<Role> roles = await _context.Roles
            .OrderBy(role => role.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return roles;
    }

    /// <inheritdoc />
    public Task<int> CountHoldersAsync(RoleId roleId, CancellationToken cancellationToken = default) =>
        _context.Users.CountAsync(
            user => user.Roles.Any(role => role.RoleId == roleId), cancellationToken);

    /// <inheritdoc />
    public void Add(Role aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.Roles.Add(aggregate);
    }

    /// <inheritdoc />
    public void Remove(Role aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _context.Roles.Remove(aggregate);
    }
}

/// <summary>Storage for the handles that keep a session alive.</summary>
public sealed class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly AccessDbContext _context;

    /// <summary>Initializes the repository.</summary>
    public RefreshTokenRepository(AccessDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public Task<RefreshToken?> FindAsync(
        string tokenHash,
        CancellationToken cancellationToken = default) =>
        _context.RefreshTokens.FirstOrDefaultAsync(
            token => token.TokenHash == tokenHash, cancellationToken);

    /// <inheritdoc />
    public void Add(RefreshToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        _context.RefreshTokens.Add(token);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Loaded and revoked one by one rather than through a bulk update, so that the change goes
    /// through the same SaveChanges as whatever caused it. A password change that committed and
    /// a session revocation that did not would be the worst possible half of this operation.
    /// </remarks>
    public async Task RevokeAllForUserAsync(
        UserId userId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        List<RefreshToken> live = await _context.RefreshTokens
            .Where(token => token.UserId == userId && token.RevokedAtUtc == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (RefreshToken token in live)
        {
            token.Revoke(now);
        }
    }

    /// <inheritdoc />
    public Task<int> PurgeExpiredAsync(
        DateTimeOffset before,
        CancellationToken cancellationToken = default) =>
        _context.RefreshTokens
            .Where(token => token.ExpiresAtUtc < before)
            .ExecuteDeleteAsync(cancellationToken);
}
