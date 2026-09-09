using AutoPartsErp.Modules.Access.Domain;
using AutoPartsErp.Modules.Access.Domain.Roles;
using AutoPartsErp.Modules.Access.Domain.Sessions;
using AutoPartsErp.Modules.Access.Domain.Users;
using AutoPartsErp.SharedKernel.Authorization;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Access.Tests;

/// <summary>
/// What a role is allowed to be.
/// <para>
/// A role is the sentence — "counter sales" — and its permissions are what that sentence means.
/// The rules here are about keeping the two honest: a role cannot carry a permission this system
/// does not have, and the administrator cannot be stripped of the one that lets anybody fix
/// anything.
/// </para>
/// </summary>
public sealed class RoleTests
{
    [Fact]
    public void A_role_carries_the_permissions_it_was_given()
    {
        Role role = NewRole();

        role.SetPermissions([Permissions.Sales.Read, Permissions.Sales.Manage])
            .IsSuccess.Should().BeTrue();

        role.Grants(Permissions.Sales.Read).Should().BeTrue();
        role.Grants(Permissions.Sales.Manage).Should().BeTrue();
        role.Grants(Permissions.Invoicing.Issue).Should().BeFalse();
    }

    /// <summary>
    /// Setting replaces rather than adds. A permission list is only reviewable if the screen that
    /// shows it is the screen that sets it.
    /// </summary>
    [Fact]
    public void Setting_permissions_replaces_what_was_there()
    {
        Role role = NewRole();
        role.SetPermissions([Permissions.Sales.Read, Permissions.Sales.Manage]);

        role.SetPermissions([Permissions.Inventory.Read]);

        role.PermissionNames.Should().ContainSingle().Which.Should().Be(Permissions.Inventory.Read);
    }

    /// <summary>
    /// A role holding a permission that does not exist grants nothing — and looks on screen
    /// exactly like a role that grants something. Refused rather than stored.
    /// </summary>
    [Fact]
    public void A_permission_this_system_does_not_have_is_refused()
    {
        Role role = NewRole();

        role.SetPermissions([Permissions.Sales.Read, "sales.order.teleport"])
            .Error.Code.Should().Be("access.role.unknown_permission");

        role.Permissions.Should().BeEmpty();
    }

    /// <summary>
    /// The one irreversible mistake this module can make. A company that removes the last right
    /// to grant rights cannot grant it back — the screen that would do it is behind the
    /// permission nobody has.
    /// </summary>
    [Fact]
    public void The_administrator_role_cannot_lose_the_right_to_grant_rights()
    {
        Role admin = Role.Create("ADMIN", "Administrator", null, isSystem: true).Value;
        admin.SetPermissions([.. Permissions.All]);

        admin.SetPermissions([Permissions.Sales.Read])
            .Error.Code.Should().Be("access.role.system_role_protected");

        admin.Grants(Permissions.Access.ManageRoles).Should().BeTrue();
    }

    [Fact]
    public void The_permission_catalogue_and_the_constants_do_not_drift()
    {
        Permissions.IsKnown(Permissions.Invoicing.Issue).Should().BeTrue();
        Permissions.IsKnown(Permissions.Access.ManageRoles).Should().BeTrue();
        Permissions.IsKnown("invoicing.document").Should().BeFalse();
        Permissions.All.Should().OnlyHaveUniqueItems();
    }

    /// <summary>The same permission twice is one permission, not two rows.</summary>
    [Fact]
    public void Setting_the_same_permission_twice_stores_it_once()
    {
        Role role = NewRole();

        role.SetPermissions([Permissions.Sales.Read, Permissions.Sales.Read]);

        role.Permissions.Should().ContainSingle();
    }

    private static Role NewRole() => Role.Create("COUNTER", "Counter sales").Value;
}

/// <summary>
/// Accounts, and the things that go wrong around them.
/// <para>
/// Nothing here touches cryptography: the aggregate stores a hash somebody else computed and
/// never compares one. What it does own is everything around the password — lockout after
/// repeated failures, the flag that says a password was chosen by somebody else, and the fact
/// that an account is closed rather than deleted.
/// </para>
/// </summary>
public sealed class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_account_is_active_and_owes_a_password_change()
    {
        User user = NewUser();

        user.IsActive.Should().BeTrue();
        user.MustChangePassword.Should().BeTrue();
        user.IsLockedOut(Now).Should().BeFalse();
        user.Roles.Should().BeEmpty();
    }

    /// <summary>
    /// Normalized on the way in, so one address cannot have two accounts and the second person
    /// to try is not told their password is wrong.
    /// </summary>
    [Fact]
    public void An_email_is_stored_lowercase_and_trimmed()
    {
        User user = User.Create("  Ana.Silva@AutoPecas.PT ", "Ana Silva", "hash").Value;

        user.Email.Should().Be("ana.silva@autopecas.pt");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ana")]
    [InlineData("@autopecas.pt")]
    [InlineData("ana@")]
    [InlineData("ana silva@autopecas.pt")]
    public void Something_that_is_not_an_address_is_refused(string email)
    {
        User.Create(email, "Ana Silva", "hash").IsFailure.Should().BeTrue();
    }

    /// <summary>
    /// Without a lockout, a sign-in endpoint is a machine for testing passwords at whatever rate
    /// the network allows.
    /// </summary>
    [Fact]
    public void Five_wrong_passwords_lock_the_account_for_fifteen_minutes()
    {
        User user = NewUser();

        for (int attempt = 0; attempt < User.MaxFailedAttempts - 1; attempt++)
        {
            user.SignInFailed(Now);
            user.IsLockedOut(Now).Should().BeFalse();
        }

        user.SignInFailed(Now);

        user.IsLockedOut(Now).Should().BeTrue();
        user.IsLockedOut(Now.AddMinutes(16)).Should().BeFalse();
        user.SignedIn(Now).Error.Code.Should().Be("access.user.locked_out");
    }

    [Fact]
    public void A_good_password_clears_the_failures_before_the_lockout_lands()
    {
        User user = NewUser();
        user.SignInFailed(Now);
        user.SignInFailed(Now);

        user.SignedIn(Now).IsSuccess.Should().BeTrue();

        user.FailedAttempts.Should().Be(0);
        user.LastSignedInAtUtc.Should().Be(Now);
    }

    [Fact]
    public void An_administrator_can_lift_a_lockout_without_waiting()
    {
        User user = NewUser();

        for (int attempt = 0; attempt < User.MaxFailedAttempts; attempt++)
        {
            user.SignInFailed(Now);
        }

        user.Unlock();

        user.IsLockedOut(Now).Should().BeFalse();
        user.SignedIn(Now).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void A_closed_account_cannot_sign_in_and_can_be_reopened()
    {
        User user = NewUser();
        user.Deactivate();

        user.IsActive.Should().BeFalse();
        user.SignedIn(Now).Error.Code.Should().Be("access.user.inactive");

        user.Reactivate();
        user.SignedIn(Now).IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// A password one person typed and another was told over the phone is a shared secret until
    /// the second one changes it — so the flag survives a reset and only a self-chosen password
    /// clears it.
    /// </summary>
    [Fact]
    public void Only_choosing_your_own_password_clears_the_must_change_flag()
    {
        User user = NewUser();

        user.ChangePassword("new-hash").IsSuccess.Should().BeTrue();
        user.MustChangePassword.Should().BeFalse();

        user.ResetPassword("administrator-set-hash");
        user.MustChangePassword.Should().BeTrue();
    }

    /// <summary>Changing a password is also how somebody gets out of a lockout they caused.</summary>
    [Fact]
    public void Changing_the_password_clears_a_lockout()
    {
        User user = NewUser();

        for (int attempt = 0; attempt < User.MaxFailedAttempts; attempt++)
        {
            user.SignInFailed(Now);
        }

        user.ChangePassword("new-hash");

        user.IsLockedOut(Now).Should().BeFalse();
    }

    [Fact]
    public void Roles_are_replaced_and_duplicates_collapse()
    {
        User user = NewUser();
        var counter = RoleId.New();

        user.SetRoles([counter, counter, RoleId.New()]);
        user.Roles.Should().HaveCount(2);

        user.SetRoles([counter]);
        user.Roles.Should().ContainSingle().Which.RoleId.Should().Be(counter);

        // Nobody's job yet is a real state, not an error.
        user.SetRoles([]);
        user.Roles.Should().BeEmpty();
    }

    [Fact]
    public void An_account_needs_a_name_and_a_password_hash()
    {
        User.Create("ana@autopecas.pt", "  ", "hash")
            .Error.Code.Should().Be("access.user.display_name_required");

        User.Create("ana@autopecas.pt", "Ana Silva", "")
            .Error.Code.Should().Be("access.user.password_required");
    }

    private static User NewUser() =>
        User.Create("ana@autopecas.pt", "Ana Silva", "stored-hash").Value;
}

/// <summary>
/// The handles that keep a session alive without a password.
/// <para>
/// Each one works exactly once. A handle presented a second time is either a copy somebody else
/// is holding or a client bug, and there is no version of either where issuing another token is
/// the right answer.
/// </para>
/// </summary>
public sealed class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_fresh_handle_is_usable_until_it_expires()
    {
        RefreshToken token = RefreshToken.Issue(
            UserId.New(), "hash", Now, TimeSpan.FromDays(14));

        token.IsUsable(Now).Should().BeTrue();
        token.IsUsable(Now.AddDays(13)).Should().BeTrue();
        token.IsUsable(Now.AddDays(15)).Should().BeFalse();
    }

    [Fact]
    public void Using_a_handle_makes_it_unusable_for_ever()
    {
        RefreshToken token = RefreshToken.Issue(
            UserId.New(), "hash", Now, TimeSpan.FromDays(14));

        token.Revoke(Now.AddHours(1));

        token.IsUsable(Now.AddHours(2)).Should().BeFalse();
        token.RevokedAtUtc.Should().Be(Now.AddHours(1));
    }

    /// <summary>
    /// Withdrawing twice keeps the first time. When the handle was spent is the interesting
    /// figure — it is what a person looking at a suspected stolen session reads.
    /// </summary>
    [Fact]
    public void Withdrawing_a_handle_twice_keeps_the_first_moment()
    {
        RefreshToken token = RefreshToken.Issue(
            UserId.New(), "hash", Now, TimeSpan.FromDays(14));

        token.Revoke(Now.AddHours(1));
        token.Revoke(Now.AddHours(5));

        token.RevokedAtUtc.Should().Be(Now.AddHours(1));
    }
}
