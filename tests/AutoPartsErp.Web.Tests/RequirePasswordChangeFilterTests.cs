using System.Security.Claims;
using AutoPartsErp.Web.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace AutoPartsErp.Web.Tests;

/// <summary>
/// The gate that keeps somebody off the system while they are still on the password an
/// administrator gave them.
/// <para>
/// The first password in this system comes out of a configuration file — a file that sits on a
/// laptop and on whatever machine it was copied to. Until it is changed, the account is as secret
/// as that file is. This is the only thing standing between the two.
/// </para>
/// </summary>
public sealed class RequirePasswordChangeFilterTests
{
    /// <summary>Somebody on a forced password is sent to the one screen that helps.</summary>
    [Theory]
    [InlineData("Home")]
    [InlineData("Parts")]
    public async Task A_forced_password_is_redirected_away_from_every_other_screen(string controller)
    {
        (IActionResult? result, bool reached) = await RunAsync(Forced(), controller);

        reached.Should().BeFalse();

        var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
        redirect.ControllerName.Should().Be("Account");
        redirect.ActionName.Should().Be("ChangePassword");
    }

    /// <summary>
    /// The account screens are the exception, and have to be.
    /// <para>
    /// They hold the change-password form itself, so redirecting them would be a redirect to
    /// itself; and they hold sign-out, so redirecting them would trap somebody who would rather
    /// just leave.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("Account")]
    [InlineData("account")]
    [InlineData("ACCOUNT")]
    public async Task The_account_screens_are_left_alone(string controller)
    {
        (IActionResult? result, bool reached) = await RunAsync(Forced(), controller);

        reached.Should().BeTrue();
        result.Should().BeNull();
    }

    /// <summary>Everybody else is left alone, which is the case that must not regress.</summary>
    [Fact]
    public async Task A_password_they_chose_is_not_interrupted()
    {
        (IActionResult? result, bool reached) = await RunAsync(SignedIn(), "Parts");

        reached.Should().BeTrue();
        result.Should().BeNull();
    }

    /// <summary>
    /// A caller with no identity at all falls through to the authorization that will refuse them.
    /// Redirecting them here would send an anonymous visitor to a change-password screen for an
    /// account that does not exist.
    /// </summary>
    [Fact]
    public async Task An_anonymous_caller_is_not_redirected()
    {
        (IActionResult? result, bool reached) = await RunAsync(new ClaimsPrincipal(), "Parts");

        reached.Should().BeTrue();
        result.Should().BeNull();
    }

    /// <summary>
    /// A request that matched no controller at all is still checked. Nothing routes that way
    /// today, and the filter should not start letting things past the day something does.
    /// </summary>
    [Fact]
    public async Task A_request_with_no_controller_is_still_redirected()
    {
        (IActionResult? result, bool reached) = await RunAsync(Forced(), controller: null);

        reached.Should().BeFalse();
        result.Should().BeOfType<RedirectToActionResult>();
    }

    /// <summary>The claim has to say true; its presence alone is not the signal.</summary>
    [Fact]
    public async Task A_claim_that_does_not_say_true_is_not_a_forced_password()
    {
        var principal = Principal(
            new Claim(ClaimTypes.Name, "Ana"),
            new Claim(ErpWebClaims.MustChangePassword, "false"));

        (IActionResult? result, bool reached) = await RunAsync(principal, "Parts");

        reached.Should().BeTrue();
        result.Should().BeNull();
    }

    private static ClaimsPrincipal Forced() =>
        Principal(
            new Claim(ClaimTypes.Name, "Ana"),
            new Claim(ErpWebClaims.MustChangePassword, "true"));

    private static ClaimsPrincipal SignedIn() =>
        Principal(new Claim(ClaimTypes.Name, "Ana"));

    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "cookie"));

    /// <summary>
    /// Runs the filter and reports both things that matter: what it did, and whether the action
    /// behind it ran. Asserting only on the result would pass for a filter that redirected and
    /// let the action run anyway.
    /// </summary>
    private static async Task<(IActionResult? Result, bool Reached)> RunAsync(
        ClaimsPrincipal user, string? controller)
    {
        var http = new DefaultHttpContext { User = user };
        var routeData = new RouteData();

        if (controller is not null)
        {
            routeData.Values["controller"] = controller;
        }

        var context = new ActionExecutingContext(
            new ActionContext(http, routeData, new ActionDescriptor()),
            [],
            new Dictionary<string, object?>(StringComparer.Ordinal),
            controller: new object());

        bool reached = false;

        await new RequirePasswordChangeFilter().OnActionExecutionAsync(context, () =>
        {
            reached = true;

            return Task.FromResult(
                new ActionExecutedContext(context, context.Filters, context.Controller));
        });

        return (context.Result, reached);
    }
}
