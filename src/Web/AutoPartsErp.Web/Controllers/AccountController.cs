using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.Web.Models;
using AutoPartsErp.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

// Aliased, because MVC has a SignInResult of its own — an action result, not an answer from the
// Access module. Importing both and hoping is how a controller ends up returning the wrong one.
using AccessSignIn = AutoPartsErp.Modules.Access.Application.Authentication.SignInResult;
using ChangeMyPasswordCommand = AutoPartsErp.Modules.Access.Application.Administration.ChangeMyPasswordCommand;
using SignInCommand = AutoPartsErp.Modules.Access.Application.Authentication.SignInCommand;

namespace AutoPartsErp.Web.Controllers;

/// <summary>Signing in and out of the browser, and changing a password.</summary>
public sealed class AccountController : Controller
{
    private readonly IDispatcher _dispatcher;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initializes the controller.</summary>
    public AccountController(IDispatcher dispatcher, ITenantContext tenantContext)
    {
        _dispatcher = dispatcher;
        _tenantContext = tenantContext;
    }

    /// <summary>Shows the sign-in form.</summary>
    /// <param name="returnUrl">Where they were going.</param>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null) =>
        View(new LoginForm { ReturnUrl = Safe(returnUrl) });

    /// <summary>Checks the password and starts the session.</summary>
    /// <param name="form">What they typed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        if (!ModelState.IsValid)
        {
            return View(form);
        }

        Result<AccessSignIn> signedIn = await _dispatcher.SendAsync(
            new SignInCommand(form.Email, form.Password), cancellationToken);

        if (signedIn.IsFailure)
        {
            // The module's own sentence, not one invented here. It already refuses to say
            // whether it was the email or the password that was wrong, which is the point.
            ModelState.AddModelError(string.Empty, signedIn.Error.Description);

            return View(form);
        }

        await HttpContext.SignInAsync(
            signedIn.Value,
            signedIn.Value.TenantId,
            _tenantContext.TenantCode,
            form.StayTrusted);

        // Straight to the change-password screen, not to where they were going. The filter would
        // send them there anyway; doing it here means they do not watch a page they asked for
        // start to load and then be taken away from them.
        if (signedIn.Value.MustChangePassword)
        {
            return RedirectToAction(nameof(ChangePassword));
        }

        return Redirect(Safe(form.ReturnUrl) ?? Url.Action(nameof(HomeController.Index), "Home")!);
    }

    /// <summary>Shows the change-password form.</summary>
    [HttpGet]
    [Authorize]
    public IActionResult ChangePassword() => View(new ChangePasswordForm());

    /// <summary>Changes the password and, if it was a forced one, lifts the block.</summary>
    /// <param name="form">What they typed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(
        ChangePasswordForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        bool forced = User.MustChangePassword();

        if (!ModelState.IsValid)
        {
            return View(form);
        }

        // The module asks for the current password even when the system is the one insisting on
        // the change: a terminal somebody walked away from is a terminal anybody can set a new
        // password on otherwise.
        Result changed = await _dispatcher.SendAsync(
            new ChangeMyPasswordCommand(form.CurrentPassword, form.NewPassword),
            cancellationToken);

        if (changed.IsFailure)
        {
            ModelState.AddModelError(string.Empty, changed.Error.Description);

            return View(form);
        }

        await HttpContext.ClearPasswordChangeAsync();

        TempData["Changed"] = true;

        return forced
            ? RedirectToAction(nameof(HomeController.Index), "Home")
            : RedirectToAction(nameof(ChangePassword));
    }

    /// <summary>Ends the session.</summary>
    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync();

        return RedirectToAction(nameof(Login));
    }

    /// <summary>Shown when somebody reaches a screen they are not allowed on.</summary>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Denied() => View();

    /// <summary>
    /// Keeps a return URL that points somewhere else out of the redirect.
    /// <para>
    /// A sign-in page that redirects wherever the query string says is one that can be linked to
    /// from an email and send somebody, freshly authenticated, to a page that is not this system.
    /// </para>
    /// </summary>
    private string? Safe(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : null;
}
