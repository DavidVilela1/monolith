namespace AutoPartsErp.SharedKernel.Results;

/// <summary>
/// The outcome of an operation that can fail for expected reasons.
/// Business failures are values, not exceptions: exceptions are reserved for bugs and
/// infrastructure faults, which keeps stack unwinding out of ordinary control flow.
/// </summary>
public class Result
{
    /// <summary>Initializes a result.</summary>
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None)
        {
            throw new InvalidOperationException("A successful result cannot carry an error.");
        }

        if (!isSuccess && error == Error.None)
        {
            throw new InvalidOperationException("A failed result must carry an error.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    /// <summary>True when the operation completed as intended.</summary>
    public bool IsSuccess { get; }

    /// <summary>True when the operation failed.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>The failure, or <see cref="Results.Error.None"/> on success.</summary>
    public Error Error { get; }

    /// <summary>A successful result with no value.</summary>
    public static Result Success() => new(true, Error.None);

    /// <summary>A failed result.</summary>
    public static Result Failure(Error error) => new(false, error);

    /// <summary>A successful result carrying a value.</summary>
    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.None);

    /// <summary>A failed result of a value-bearing operation.</summary>
    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);

    /// <summary>
    /// Wraps an error in a failed result, so a guard clause can read
    /// <c>return CatalogErrors.Part.NameRequired;</c> instead of restating the ceremony.
    /// </summary>
    public static implicit operator Result(Error error) => Failure(error);

    /// <summary>Named alternative to the implicit conversion from <see cref="Results.Error"/>.</summary>
    public static Result FromError(Error error) => Failure(error);

    /// <summary>Returns the first failure among the supplied results, or success if all succeeded.</summary>
    public static Result FirstFailureOrSuccess(params Result[] results)
    {
        ArgumentNullException.ThrowIfNull(results);
        foreach (Result result in results)
        {
            if (result.IsFailure)
            {
                return Failure(result.Error);
            }
        }

        return Success();
    }
}

/// <summary>An outcome that carries a value when it succeeds.</summary>
/// <typeparam name="TValue">The type produced on success.</typeparam>
public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    internal Result(TValue? value, bool isSuccess, Error error)
        : base(isSuccess, error)
    {
        _value = value;
    }

    /// <summary>The produced value. Throws when the result is a failure.</summary>
    /// <exception cref="InvalidOperationException">The result is a failure.</exception>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Cannot read the value of a failed result ({Error.Code}).");

    /// <summary>Wraps a value in a successful result.</summary>
    public static implicit operator Result<TValue>(TValue value) => Success(value);

    /// <summary>Wraps an error in a failed result.</summary>
    public static implicit operator Result<TValue>(Error error) => Failure<TValue>(error);

    /// <summary>Projects the value when successful, propagating the failure otherwise.</summary>
    public Result<TOut> Map<TOut>(Func<TValue, TOut> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return IsSuccess ? Success(map(Value)) : Failure<TOut>(Error);
    }

    /// <summary>Chains another fallible operation, propagating the failure otherwise.</summary>
    public Result<TOut> Bind<TOut>(Func<TValue, Result<TOut>> bind)
    {
        ArgumentNullException.ThrowIfNull(bind);
        return IsSuccess ? bind(Value) : Failure<TOut>(Error);
    }

    /// <summary>Collapses both branches into a single value.</summary>
    public TOut Match<TOut>(Func<TValue, TOut> onSuccess, Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return IsSuccess ? onSuccess(Value) : onFailure(Error);
    }
}
