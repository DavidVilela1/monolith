using System.Security.Claims;
using AutoPartsErp.Modules.Abstractions.Http;
using AutoPartsErp.SharedKernel.Authorization;
using AutoPartsErp.Web.Security;

namespace AutoPartsErp.Web.Tests;

/// <summary>
/// What a view is allowed to ask about whoever is looking at it.
/// <para>
/// These read the cookie's claims, and every one of them decides what a menu offers. A menu that
/// offers a screen the person will be refused on is worse than one that hides it: the refusal
/// arrives after they have decided to do something, and it reads as a broken system rather than
/// as a rule.
/// </para>
/// </summary>
public sealed class CurrentUserTests
{
    /// <summary>A permission they hold is a permission they hold.</summary>
    [Fact]
    public void A_held_permission_is_granted()
    {
        ClaimsPrincipal user = SignedIn(Permissions.Catalog.Read);

        user.Can(Permissions.Catalog.Read).Should().BeTrue();
        user.Can(Permissions.Catalog.Manage).Should().BeFalse();
    }

    /// <summary>
    /// Nobody signed in holds nothing, even if a claim somehow says otherwise. A principal with
    /// permissions and no authenticated identity is what an unauthenticated request looks like
    /// after somebody has been careless, and it must not open a menu.
    /// </summary>
    [Fact]
    public void An_unauthenticated_principal_holds_nothing()
    {
        var user = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ErpClaims.Permission, Permissions.Catalog.Read)]));

        user.Identity!.IsAuthenticated.Should().BeFalse();
        user.Can(Permissions.Catalog.Read).Should().BeFalse();
    }

    /// <summary>Nothing at all is refused rather than throwing.</summary>
    [Fact]
    public void Nobody_holds_nothing()
    {
        ClaimsPrincipal? nobody = null;

        nobody.Can(Permissions.Catalog.Read).Should().BeFalse();
        nobody.CanAny(Permissions.Catalog.Read, Permissions.Sales.Read).Should().BeFalse();
        nobody.MustChangePassword().Should().BeFalse();
        nobody.DisplayName().Should().BeEmpty();
        nobody.Initials().Should().Be("?");
    }

    /// <summary>Any one of several is enough, and none of them is not.</summary>
    [Fact]
    public void Any_one_of_several_is_enough()
    {
        ClaimsPrincipal user = SignedIn(Permissions.Sales.Read);

        user.CanAny(Permissions.Catalog.Read, Permissions.Sales.Read).Should().BeTrue();
        user.CanAny(Permissions.Catalog.Read, Permissions.Finance.Read).Should().BeFalse();
        user.CanAny().Should().BeFalse();
    }

    /// <summary>
    /// Two letters, from the first word and the last.
    /// <para>
    /// Not the first two words: "Duarte Miguel Vilela" and "Duarte Vilela" are the same person,
    /// and initials that changed because somebody typed a middle name one week and not the next
    /// would be a different avatar for the same account.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("Duarte Vilela", "DV")]
    [InlineData("Duarte Miguel Vilela", "DV")]
    [InlineData("  Duarte   Vilela  ", "DV")]
    [InlineData("ana", "A")]
    [InlineData("", "?")]
    [InlineData("   ", "?")]
    public void Initials_come_from_the_first_and_last_word(string name, string expected)
    {
        Named(name).Initials().Should().Be(expected);
    }

    /// <summary>The mark that forces a password change is read off the claim, and only when true.</summary>
    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("1", false)]
    public void The_forced_password_mark_has_to_say_true(string value, bool expected)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ErpWebClaims.MustChangePassword, value)], "cookie"));

        user.MustChangePassword().Should().Be(expected);
    }

    private static ClaimsPrincipal SignedIn(params string[] permissions) =>
        new(new ClaimsIdentity(
            [.. permissions.Select(permission => new Claim(ErpClaims.Permission, permission))],
            "cookie"));

    private static ClaimsPrincipal Named(string name) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, name)], "cookie"));
}
