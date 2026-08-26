namespace AutoPartsErp.SharedKernel.Results;

/// <summary>A single field-level problem with a request.</summary>
/// <param name="PropertyName">The offending property, in the shape the client sent it.</param>
/// <param name="Code">Stable code for the rule that failed.</param>
/// <param name="Message">Human-readable explanation.</param>
public sealed record ValidationFailure(string PropertyName, string Code, string Message);

/// <summary>
/// An <see cref="Error"/> that carries the full list of field-level problems, so a client
/// can highlight every bad field at once instead of discovering them one round trip at a time.
/// </summary>
public sealed record ValidationError : Error
{
    /// <summary>The stable code used for all aggregate validation failures.</summary>
    public const string ValidationCode = "validation.failed";

    /// <summary>Creates a validation error from the supplied failures.</summary>
    public ValidationError(IReadOnlyList<ValidationFailure> failures)
        : base(ValidationCode, BuildDescription(failures), ErrorType.Validation)
    {
        Failures = failures;
    }

    /// <summary>The individual field failures.</summary>
    public IReadOnlyList<ValidationFailure> Failures { get; }

    private static string BuildDescription(IReadOnlyList<ValidationFailure> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);
        return failures.Count == 1
            ? failures[0].Message
            : $"The request failed validation with {failures.Count} problems.";
    }
}

/// <summary>Validates a request before its handler runs.</summary>
/// <typeparam name="T">The request type validated.</typeparam>
public interface IValidator<in T>
{
    /// <summary>Returns every problem found, or an empty list when the request is valid.</summary>
    ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(T instance, CancellationToken cancellationToken = default);
}
