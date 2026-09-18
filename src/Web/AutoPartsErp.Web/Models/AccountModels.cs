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

/// <summary>What the change-password form asks for.</summary>
public sealed class ChangePasswordForm
{
    /// <summary>
    /// The shortest password this system accepts.
    /// <para>
    /// Twelve, which is the Access module's rule. Written here as well so the form can say so
    /// before somebody types, rather than refusing them afterwards — and the module still checks,
    /// because a rule enforced only by a screen is a rule the API does not have.
    /// </para>
    /// </summary>
    public const int MinimumLength = 12;

    /// <summary>The password they have now.</summary>
    [Required(ErrorMessage = "Indique a palavra-passe atual.")]
    [DataType(DataType.Password)]
    [Display(Name = "Palavra-passe atual")]
    public string CurrentPassword { get; set; } = string.Empty;

    /// <summary>The one they want.</summary>
    [Required(ErrorMessage = "Indique a nova palavra-passe.")]
    [MinLength(MinimumLength, ErrorMessage = "A nova palavra-passe precisa de pelo menos 12 caracteres.")]
    [DataType(DataType.Password)]
    [Display(Name = "Nova palavra-passe")]
    public string NewPassword { get; set; } = string.Empty;

    /// <summary>
    /// The same one again.
    /// <para>
    /// Asked for because the field is masked: somebody who mistypes a password they cannot see is
    /// somebody locked out of a system whose only other way in is an administrator.
    /// </para>
    /// </summary>
    [Required(ErrorMessage = "Repita a nova palavra-passe.")]
    [Compare(nameof(NewPassword), ErrorMessage = "As duas não coincidem.")]
    [DataType(DataType.Password)]
    [Display(Name = "Repetir a nova")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
