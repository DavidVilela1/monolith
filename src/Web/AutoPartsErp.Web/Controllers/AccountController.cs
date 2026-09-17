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
using SignInCommand = AutoPartsErp.Modules.Access.Application.Authentication.SignInCommand;

namespace AutoPartsErp.Web.Controllers;

/// <summary>Signing in and out of the browser.</summary>
[AllowAnonymous]
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
    public IActionResult Login(string? returnUrl = null) =>
        View(new LoginForm { ReturnUrl = Safe(returnUrl) });

    /// <summary>Checks the password and starts the session.</summary>
    /// <param name="form">What they typed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost]
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

        return Redirect(Safe(form.ReturnUrl) ?? Url.Action(nameof(HomeController.Index), "Home")!);
    }

    /// <summary>Ends the session.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync();

        return RedirectToAction(nameof(Login));
    }

    /// <summary>Shown when somebody reaches a screen they are not allowed on.</summary>
    [HttpGet]
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
