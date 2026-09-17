using System.ComponentModel.DataAnnotations;

namespace AutoPartsErp.Web.Models;

/// <summary>What the sign-in form asks for.</summary>
public sealed class LoginForm
{
    /// <summary>The email they sign in with.</summary>
    [Required(ErrorMessage = "Indique o email.")]
    [EmailAddress(ErrorMessage = "Esse email não parece um email.")]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    /// <summary>Their password.</summary>
    [Required(ErrorMessage = "Indique a palavra-passe.")]
    [DataType(DataType.Password)]
    [Display(Name = "Palavra-passe")]
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Whether to keep the session after the browser closes.
    /// <para>
    /// Off by default. A shared counter terminal is the ordinary case in a parts business, and
    /// the ordinary case should be the safe one.
    /// </para>
    /// </summary>
    [Display(Name = "Manter a sessão iniciada")]
    public bool StayTrusted { get; set; }

    /// <summary>Where they were going before they were asked to sign in.</summary>
    public string? ReturnUrl { get; set; }
}
