using System.Collections.Concurrent;
using System.Reflection;

namespace AutoPartsErp.SharedKernel.Results;

/// <summary>
/// Builds a failed <see cref="Result"/> or <see cref="Result{TValue}"/> when the concrete
/// response type is only known at runtime. Used by pipeline behaviours, which must short-circuit
/// a handler without knowing whether it returns a value.
/// </summary>
public static class ResultFactory
{
    private static readonly ConcurrentDictionary<Type, Func<Error, object>> FailureFactories = new();

    /// <summary>True when <typeparamref name="TResponse"/> is <see cref="Result"/> or <see cref="Result{TValue}"/>.</summary>
    public static bool IsResultType<TResponse>() => typeof(Result).IsAssignableFrom(typeof(TResponse));

    /// <summary>
    /// Creates a failed result of the requested response type.
    /// </summary>
    /// <typeparam name="TResponse">Either <see cref="Result"/> or <see cref="Result{TValue}"/>.</typeparam>
    /// <param name="error">The failure to report.</param>
    /// <exception cref="InvalidOperationException"><typeparamref name="TResponse"/> is not a result type.</exception>
    public static TResponse Failure<TResponse>(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        Func<Error, object> factory = FailureFactories.GetOrAdd(typeof(TResponse), BuildFactory);
        return (TResponse)factory(error);
    }

    private static Func<Error, object> BuildFactory(Type responseType)
    {
        if (responseType == typeof(Result))
        {
            return static error => Result.Failure(error);
        }

        if (responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(Result<>))
        {
            Type valueType = responseType.GetGenericArguments()[0];

            MethodInfo generic = typeof(Result)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(m => m.Name == nameof(Result.Failure) && m.IsGenericMethodDefinition)
                .MakeGenericMethod(valueType);

            return error => generic.Invoke(null, [error])!;
        }

        throw new InvalidOperationException(
            $"'{responseType.FullName}' is not a Result type. Handlers must return Result or Result<T> " +
            "so that pipeline behaviours can short-circuit without throwing.");
    }
}
