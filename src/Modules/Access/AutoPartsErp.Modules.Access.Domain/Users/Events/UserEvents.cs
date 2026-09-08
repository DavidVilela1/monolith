using AutoPartsErp.SharedKernel.Primitives;

namespace AutoPartsErp.Modules.Access.Domain.Users.Events;

/// <summary>Raised when an account is opened.</summary>
/// <param name="UserId">The new user.</param>
/// <param name="Email">How they sign in.</param>
/// <param name="DisplayName">Their name.</param>
public sealed record UserCreatedDomainEvent(
    UserId UserId,
    string Email,
    string DisplayName) : DomainEvent;

/// <summary>
/// Raised when repeated wrong passwords lock an account.
/// <para>
/// Worth being an event rather than a log line: five failures in a row against one account is
/// either somebody who forgot their password or somebody guessing, and only one of those should
/// reach a person. Nothing consumes it yet.
/// </para>
/// </summary>
/// <param name="UserId">The account.</param>
/// <param name="Email">Whose it is.</param>
/// <param name="LockedUntilUtc">When the lockout lifts.</param>
public sealed record UserLockedOutDomainEvent(
    UserId UserId,
    string Email,
    DateTimeOffset LockedUntilUtc) : DomainEvent;

/// <summary>Raised when an account is closed.</summary>
/// <param name="UserId">The account.</param>
/// <param name="Email">Whose it was.</param>
public sealed record UserDeactivatedDomainEvent(UserId UserId, string Email) : DomainEvent;
