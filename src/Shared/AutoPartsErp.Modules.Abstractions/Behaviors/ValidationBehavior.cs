using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Abstractions.Behaviors;

/// <summary>
/// Runs every registered validator for a request before its handler executes, and
/// short-circuits with a <see cref="ValidationError"/> when any of them object.
/// <para>
/// Validation lives here rather than in the handler so that handlers only ever deal with
/// input they can trust, and so a client gets every bad field back in one response.
/// </para>
/// </summary>
/// <typeparam name="TRequest">The request being validated.</typeparam>
/// <typeparam name="TResponse">The handler's response type, which must be a result type.</typeparam>
public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    /// <summary>Initializes the behaviour with every validator registered for the request.</summary>
    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators ?? throw new ArgumentNullException(nameof(validators));
    }

    /// <inheritdoc />
    public async Task<TResponse> HandleAsync(
        TRequest request,
        Func<Task<TResponse>> next,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(next);

        var failures = new List<ValidationFailure>();

        foreach (IValidator<TRequest> validator in _validators)
        {
            IReadOnlyList<ValidationFailure> result =
                await validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);

            if (result.Count > 0)
            {
                failures.AddRange(result);
            }
        }

        if (failures.Count == 0)
        {
            return await next().ConfigureAwait(false);
        }

        return ResultFactory.Failure<TResponse>(new ValidationError(failures));
    }
}
