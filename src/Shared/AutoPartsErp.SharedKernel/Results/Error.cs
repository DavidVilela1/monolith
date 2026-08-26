namespace AutoPartsErp.SharedKernel.Results;

/// <summary>Classifies a failure so the API layer can map it to the right HTTP status code.</summary>
public enum ErrorType
{
    /// <summary>No failure.</summary>
    None = 0,

    /// <summary>Input did not satisfy the contract. Maps to 400.</summary>
    Validation = 1,

    /// <summary>The requested resource does not exist. Maps to 404.</summary>
    NotFound = 2,

    /// <summary>The request contradicts current state, e.g. a duplicate SKU. Maps to 409.</summary>
    Conflict = 3,

    /// <summary>A domain rule forbids the operation. Maps to 422.</summary>
    DomainRule = 4,

    /// <summary>The caller is not permitted to do this. Maps to 403.</summary>
    Forbidden = 5,

    /// <summary>Something went wrong that the caller cannot fix. Maps to 500.</summary>
    Unexpected = 6,
}

/// <summary>
/// A machine-readable failure. Errors carry a stable dotted code (<c>catalog.part.sku_taken</c>)
/// so clients and translations can key off it instead of parsing English text.
/// </summary>
/// <param name="Code">Stable, dotted, lowercase identifier for the failure.</param>
/// <param name="Description">Human-readable explanation, safe to show to an operator.</param>
/// <param name="Type">The failure classification.</param>
public record Error(string Code, string Description, ErrorType Type = ErrorType.Unexpected)
{
    /// <summary>The absence of an error.</summary>
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.None);

    /// <summary>Creates a validation error.</summary>
    public static Error Validation(string code, string description) =>
        new(code, description, ErrorType.Validation);

    /// <summary>Creates a not-found error.</summary>
    public static Error NotFound(string code, string description) =>
        new(code, description, ErrorType.NotFound);

    /// <summary>Creates a conflict error.</summary>
    public static Error Conflict(string code, string description) =>
        new(code, description, ErrorType.Conflict);

    /// <summary>Creates a domain rule violation.</summary>
    public static Error DomainRule(string code, string description) =>
        new(code, description, ErrorType.DomainRule);

    /// <summary>Creates a permission error.</summary>
    public static Error Forbidden(string code, string description) =>
        new(code, description, ErrorType.Forbidden);

    /// <summary>Creates an unexpected error.</summary>
    public static Error Unexpected(string code, string description) =>
        new(code, description, ErrorType.Unexpected);

    /// <inheritdoc />
    public override string ToString() => $"{Code}: {Description}";
}
