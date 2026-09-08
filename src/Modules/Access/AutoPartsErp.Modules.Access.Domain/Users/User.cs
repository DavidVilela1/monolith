using AutoPartsErp.Modules.Access.Domain.Users.Events;
using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Access.Domain.Users;

/// <summary>
/// Somebody who may use this system, and the roles that say what they may do.
/// <para>
/// A user belongs to one tenant. Somebody who works for two companies in a group has two logins,
/// which is a real inconvenience and the honest trade: a membership table would let one identity
/// span companies, and would also make every query in this module ask which company it means.
/// The day a group needs it, this is the aggregate that changes; nothing else does, because
/// everything downstream reads the tenant from a claim.
/// </para>
/// <para>
/// <b>No cryptography lives here.</b> <see cref="PasswordHash"/> is an opaque string this project
/// stores and compares nothing against. The hashing is done in the infrastructure by the
/// framework's <c>PasswordHasher</c>, and keeping the domain ignorant of it is what stops
/// somebody one day writing a helpful little comparison that leaks a timing signal.
/// </para>
/// </summary>
public sealed class User : AggregateRoot<UserId>, IAuditable, ITenantScoped
{
    /// <summary>Longest permitted email address.</summary>
    public const int MaxEmailLength = 256;

    /// <summary>Longest permitted display name.</summary>
    public const int MaxDisplayNameLength = 120;

    /// <summary>How many wrong passwords in a row lock the account.</summary>
    public const int MaxFailedAttempts = 5;

    /// <summary>How long the account stays locked.</summary>
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly List<UserRole> _roles = [];

    private User(UserId id, string email, string displayName, string passwordHash)
        : base(id)
    {
        Email = email;
        DisplayName = displayName;
        PasswordHash = passwordHash;
        IsActive = true;
        CreatedBy = string.Empty;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private User()
    {
    }
#pragma warning restore CS8618

    /// <summary>How they sign in. Normalized lowercase, unique within the tenant.</summary>
    public string Email { get; private set; } = string.Empty;

    /// <summary>Their name, as it appears on a document and in an audit trail.</summary>
    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>
    /// An opaque string produced by whatever hashes passwords. Never compared here.
    /// </summary>
    public string PasswordHash { get; private set; } = string.Empty;

    /// <summary>False once somebody has left. Deactivated rather than deleted, because their name is on documents.</summary>
    public bool IsActive { get; private set; }

    /// <summary>True while the password is the one an administrator set and the user has not replaced it.</summary>
    public bool MustChangePassword { get; private set; }

    /// <summary>How many wrong passwords in a row.</summary>
    public int FailedAttempts { get; private set; }

    /// <summary>When the lockout lifts, while one is in force.</summary>
    public DateTimeOffset? LockedUntilUtc { get; private set; }

    /// <summary>When they last got in.</summary>
    public DateTimeOffset? LastSignedInAtUtc { get; private set; }

    /// <summary>The roles they hold.</summary>
    public IReadOnlyCollection<UserRole> Roles => _roles.AsReadOnly();

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

    /// <summary>True while the account is locked out after repeated failures.</summary>
    public bool IsLockedOut(DateTimeOffset now) =>
        LockedUntilUtc is { } until && until > now;

    /// <summary>Creates a user with a password somebody else chose for them.</summary>
    /// <param name="email">Their sign-in address.</param>
    /// <param name="displayName">Their name.</param>
    /// <param name="passwordHash">The hash of the password they were given.</param>
    /// <param name="mustChangePassword">
    /// True when an administrator set the password. The default, because a password one person
    /// typed and another was told over the phone is a shared secret until the second one changes
    /// it.
    /// </param>
    public static Result<User> Create(
        string email,
        string displayName,
        string passwordHash,
        bool mustChangePassword = true)
    {
        Result<string> normalized = NormalizeEmail(email);

        if (normalized.IsFailure)
        {
            return Result.Failure<User>(normalized.Error);
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            return AccessErrors.User.DisplayNameRequired;
        }

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            return AccessErrors.User.PasswordRequired;
        }

        string name = displayName.Trim();

        var user = new User(
            UserId.New(),
            normalized.Value,
            name[..Math.Min(name.Length, MaxDisplayNameLength)],
            passwordHash)
        {
            MustChangePassword = mustChangePassword,
        };

        user.Raise(new UserCreatedDomainEvent(user.Id, user.Email, user.DisplayName));

        return user;
    }

    /// <summary>
    /// Records a successful sign-in and clears any failure count.
    /// <para>
    /// The caller has already checked the password. This aggregate is deliberately not told what
    /// was typed — it records the outcome, so that nothing here can ever be the place a secret
    /// leaks from.
    /// </para>
    /// </summary>
    public Result SignedIn(DateTimeOffset now)
    {
        if (!IsActive)
        {
            return AccessErrors.User.Inactive;
        }

        if (IsLockedOut(now))
        {
            return AccessErrors.User.LockedOut(LockedUntilUtc!.Value);
        }

        FailedAttempts = 0;
        LockedUntilUtc = null;
        LastSignedInAtUtc = now;

        return Result.Success();
    }

    /// <summary>
    /// Records a wrong password, and locks the account after enough of them.
    /// <para>
    /// Without this a login endpoint is a machine for testing passwords at whatever rate the
    /// network allows. Fifteen minutes is short enough that a person who fat-fingered their own
    /// password goes for a coffee, and long enough that guessing stops being worth doing.
    /// </para>
    /// </summary>
    public void SignInFailed(DateTimeOffset now)
    {
        FailedAttempts++;

        if (FailedAttempts >= MaxFailedAttempts)
        {
            LockedUntilUtc = now.Add(LockoutDuration);
            FailedAttempts = 0;

            Raise(new UserLockedOutDomainEvent(Id, Email, LockedUntilUtc.Value));
        }
    }

    /// <summary>Lifts a lockout, for when the person rings up rather than waiting.</summary>
    public void Unlock()
    {
        FailedAttempts = 0;
        LockedUntilUtc = null;
    }

    /// <summary>Replaces the password with one the user chose themselves.</summary>
    /// <param name="passwordHash">The hash of the new password.</param>
    public Result ChangePassword(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            return AccessErrors.User.PasswordRequired;
        }

        PasswordHash = passwordHash;
        MustChangePassword = false;
        FailedAttempts = 0;
        LockedUntilUtc = null;

        return Result.Success();
    }

    /// <summary>Sets a password on somebody's behalf. They will have to change it.</summary>
    /// <param name="passwordHash">The hash of the password they were given.</param>
    public Result ResetPassword(string passwordHash)
    {
        Result changed = ChangePassword(passwordHash);

        if (changed.IsFailure)
        {
            return changed;
        }

        MustChangePassword = true;

        return Result.Success();
    }

    /// <summary>Corrects their name.</summary>
    public Result Rename(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return AccessErrors.User.DisplayNameRequired;
        }

        string name = displayName.Trim();
        DisplayName = name[..Math.Min(name.Length, MaxDisplayNameLength)];

        return Result.Success();
    }

    /// <summary>
    /// Replaces the roles this user holds.
    /// <para>
    /// A user with no roles can sign in and do nothing, which is the right shape for somebody
    /// whose account exists before anybody has decided what they do. It is not an error.
    /// </para>
    /// </summary>
    public void SetRoles(IReadOnlyCollection<RoleId> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);

        _roles.Clear();

        foreach (RoleId role in roles.Distinct())
        {
            _roles.Add(new UserRole(role));
        }
    }

    /// <summary>
    /// Closes the account. Never deleted: their name is on documents that outlive them.
    /// </summary>
    public void Deactivate()
    {
        IsActive = false;

        Raise(new UserDeactivatedDomainEvent(Id, Email));
    }

    /// <summary>Reopens a closed account.</summary>
    public void Reactivate()
    {
        IsActive = true;
        FailedAttempts = 0;
        LockedUntilUtc = null;
    }

    private static Result<string> NormalizeEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return AccessErrors.User.EmailRequired;
        }

        string trimmed = email.Trim().ToLowerInvariant();

        if (trimmed.Length > MaxEmailLength)
        {
            return AccessErrors.User.EmailTooLong;
        }

        // Deliberately shallow. A regular expression that decides what an address may look like
        // is a regular expression that eventually rejects a real customer's real address; the
        // only test that means anything is whether mail arrives, and this module does not send
        // any. What it does need is something with one @ in the middle, so a username cannot be
        // typed here by mistake.
        int at = trimmed.IndexOf('@', StringComparison.Ordinal);

        return at > 0 && at < trimmed.Length - 1 && trimmed.IndexOf(' ', StringComparison.Ordinal) < 0
            ? trimmed
            : AccessErrors.User.EmailInvalid;
    }
}

/// <summary>A role held by a user.</summary>
/// <param name="RoleId">The role.</param>
public sealed record UserRole(RoleId RoleId);
