using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.SharedKernel.Messaging;

/// <summary>A request that produces a response of type <typeparamref name="TResponse"/>.</summary>
/// <typeparam name="TResponse">The response type.</typeparam>
#pragma warning disable CA1040 // Marker interface is the point: it carries the response type.
public interface IRequest<out TResponse>;
#pragma warning restore CA1040

/// <summary>An instruction that changes state and returns only success or failure.</summary>
public interface ICommand : IRequest<Result>;

/// <summary>An instruction that changes state and returns a value on success.</summary>
/// <typeparam name="TResponse">The value produced on success.</typeparam>
public interface ICommand<TResponse> : IRequest<Result<TResponse>>;

/// <summary>A read that never changes state.</summary>
/// <typeparam name="TResponse">The value produced on success.</typeparam>
public interface IQuery<TResponse> : IRequest<Result<TResponse>>;

/// <summary>Handles a request.</summary>
/// <typeparam name="TRequest">The request handled.</typeparam>
/// <typeparam name="TResponse">The response produced.</typeparam>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>Executes the request.</summary>
    Task<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Handles a command that returns no value.</summary>
/// <typeparam name="TCommand">The command handled.</typeparam>
public interface ICommandHandler<in TCommand> : IRequestHandler<TCommand, Result>
    where TCommand : ICommand;

/// <summary>Handles a command that returns a value.</summary>
/// <typeparam name="TCommand">The command handled.</typeparam>
/// <typeparam name="TResponse">The value produced on success.</typeparam>
public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, Result<TResponse>>
    where TCommand : ICommand<TResponse>;

/// <summary>Handles a query.</summary>
/// <typeparam name="TQuery">The query handled.</typeparam>
/// <typeparam name="TResponse">The value produced on success.</typeparam>
public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse>;

/// <summary>
/// A cross-cutting step that wraps every handler: logging, validation, transactions, metrics.
/// Behaviours run in registration order, outermost first.
/// </summary>
/// <typeparam name="TRequest">The request type the behaviour applies to.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>Runs before and/or after the rest of the pipeline.</summary>
    /// <param name="request">The request being handled.</param>
    /// <param name="next">Invokes the remainder of the pipeline.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<TResponse> HandleAsync(
        TRequest request,
        Func<Task<TResponse>> next,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Sends a request to its single registered handler, through the behaviour pipeline.
/// Callers depend on this, never on a concrete handler, which is what keeps modules decoupled.
/// </summary>
public interface IDispatcher
{
    /// <summary>Dispatches a request and returns its response.</summary>
    /// <exception cref="InvalidOperationException">No handler is registered for the request.</exception>
    Task<TResponse> SendAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default);
}
