using System.Globalization;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Access.Domain;

/// <summary>Every failure the Access module can report, in one place.</summary>
public static class AccessErrors
{
    /// <summary>Failures relating to a <see cref="Users.User"/>.</summary>
    public static class User
    {
        /// <summary>No such user.</summary>
        public static Error NotFound(string identifier) =>
            Error.NotFound("access.user.not_found", $"No user matches '{identifier}'.");

        /// <summary>The email is already in use in this tenant.</summary>
        public static Error EmailAlreadyExists(string email) =>
            Error.Conflict("access.user.email_exists", $"'{email}' already has an account here.");

        /// <summary>An email address is required.</summary>
        public static readonly Error EmailRequired =
            Error.Validation("access.user.email_required", "An email address is required.");

        /// <summary>The email address does not look like one.</summary>
        public static readonly Error EmailInvalid =
            Error.Validation(
                "access.user.email_invalid",
                "That does not look like an email address. It needs a name, an @ and a domain.");

        /// <summary>The email address is too long.</summary>
        public static readonly Error EmailTooLong =
            Error.Validation(
                "access.user.email_too_long", "An email address may be at most 256 characters.");

        /// <summary>A display name is required.</summary>
        public static readonly Error DisplayNameRequired =
            Error.Validation(
                "access.user.display_name_required",
                "A name is required. It goes on every document this person issues.");

        /// <summary>A password is required.</summary>
        public static readonly Error PasswordRequired =
            Error.Validation("access.user.password_required", "A password is required.");

        /// <summary>The password is not long enough to be worth having.</summary>
        public static readonly Error PasswordTooShort =
            Error.Validation(
                "access.user.password_too_short",
                "A password must be at least 12 characters. Length is the only property of a "
                + "password that reliably makes it harder to guess.");

        /// <summary>
        /// The credentials do not match.
        /// <para>
        /// One error for "no such account" and "wrong password", deliberately. Telling them apart
        /// turns the login endpoint into a way of finding out who has an account here, which is
        /// the first thing anybody attacking it wants to know.
        /// </para>
        /// </summary>
        public static readonly Error InvalidCredentials =
            Error.DomainRule(
                "access.user.invalid_credentials", "That email and password do not match an account.");

        /// <summary>The account has been closed.</summary>
        public static readonly Error Inactive =
            Error.DomainRule(
                "access.user.inactive", "That account has been closed. Ask an administrator to reopen it.");

        /// <summary>Too many wrong passwords.</summary>
        public static Error LockedOut(DateTimeOffset until) =>
            Error.DomainRule(
                "access.user.locked_out",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"That account is locked after too many failed attempts. Try again after {until:HH:mm} UTC, or ask an administrator to unlock it."));

        /// <summary>The current password given does not match.</summary>
        public static readonly Error CurrentPasswordWrong =
            Error.DomainRule(
                "access.user.current_password_wrong", "The current password is not right.");

        /// <summary>Somebody tried to close the last account that can administer the system.</summary>
        public static readonly Error LastAdministrator =
            Error.DomainRule(
                "access.user.last_administrator",
                "That is the last account that can manage roles. Closing it would lock this "
                + "company out of its own system with no way back in.");
    }

    /// <summary>Failures relating to a <see cref="Roles.Role"/>.</summary>
    public static class Role
    {
        /// <summary>No such role.</summary>
        public static Error NotFound(string identifier) =>
            Error.NotFound("access.role.not_found", $"No role matches '{identifier}'.");

        /// <summary>The code is already in use.</summary>
        public static Error CodeAlreadyExists(string code) =>
            Error.Conflict("access.role.code_exists", $"Role code '{code}' is already in use.");

        /// <summary>A code is required.</summary>
        public static readonly Error CodeRequired =
            Error.Validation("access.role.code_required", "A role code is required.");

        /// <summary>A name is required.</summary>
        public static readonly Error NameRequired =
            Error.Validation("access.role.name_required", "A role name is required.");

        /// <summary>The permission is not one this system recognizes.</summary>
        public static Error UnknownPermission(string permission) =>
            Error.Validation(
                "access.role.unknown_permission",
                $"'{permission}' is not a permission this system has. A role holding one that "
                + "does not exist grants nothing, and looks on screen exactly like one that does.");

        /// <summary>The administrator role cannot be stripped of the right to grant rights.</summary>
        public static readonly Error SystemRoleCannotLosePermissions =
            Error.DomainRule(
                "access.role.system_role_protected",
                "The administrator role has to keep the right to manage roles. Without it, the "
                + "next person who needs a password reset is locked out of a system nobody can "
                + "get into.");

        /// <summary>A system role cannot be deleted.</summary>
        public static readonly Error SystemRoleCannotBeDeleted =
            Error.DomainRule(
                "access.role.system_role_undeletable", "The administrator role cannot be deleted.");

        /// <summary>The role is held by somebody.</summary>
        public static Error StillHeld(int holders) =>
            Error.Conflict(
                "access.role.still_held",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{holders} people still hold that role. Move them off it first, so that nobody silently loses access."));
    }

    /// <summary>Failures relating to tokens.</summary>
    public static class Token
    {
        /// <summary>The refresh token is unknown, expired, or has already been used.</summary>
        public static readonly Error RefreshRejected =
            Error.DomainRule(
                "access.token.refresh_rejected",
                "That session cannot be renewed. Sign in again.");
    }
}
