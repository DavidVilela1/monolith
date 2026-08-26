using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;

namespace AutoPartsErp.SharedKernel.Messaging;

/// <summary>
/// Resolves the handler for a request from the container and runs it through every
/// registered <see cref="IPipelineBehavior{TRequest,TResponse}"/>.
/// <para>
/// This is a deliberate 40-line replacement for a mediator library: it depends only on
/// <see cref="IServiceProvider"/> from the base class library, so the SharedKernel stays
/// free of third-party packages and licence changes upstream cannot strand the codebase.
/// </para>
/// </summary>
public sealed class Dispatcher : IDispatcher
{
    private static readonly ConcurrentDictionary<Type, RequestExecutor> ExecutorCache = new();

    private readonly IServiceProvider _serviceProvider;

    /// <summary>Initializes the dispatcher with the container it resolves handlers from.</summary>
    public Dispatcher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <inheritdoc />
    public async Task<TResponse> SendAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        RequestExecutor executor = ExecutorCache.GetOrAdd(
            request.GetType(),
            static requestType => CreateExecutor(requestType, typeof(TResponse)));

        object? response = await executor
            .ExecuteAsync(request, _serviceProvider, cancellationToken)
            .ConfigureAwait(false);

        return (TResponse)response!;
    }

    private static RequestExecutor CreateExecutor(Type requestType, Type responseType)
    {
        Type executorType = typeof(RequestExecutor<,>).MakeGenericType(requestType, responseType);
        return (RequestExecutor)Activator.CreateInstance(executorType)!;
    }

    private abstract class RequestExecutor
    {
        public abstract Task<object?> ExecuteAsync(
            object request,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken);
    }

    private sealed class RequestExecutor<TRequest, TResponse> : RequestExecutor
        where TRequest : IRequest<TResponse>
    {
        public override async Task<object?> ExecuteAsync(
            object request,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken)
        {
            var typedRequest = (TRequest)request;

            var handler = serviceProvider.GetService(typeof(IRequestHandler<TRequest, TResponse>))
                as IRequestHandler<TRequest, TResponse>
                ?? throw new InvalidOperationException(
                    $"No handler is registered for '{typeof(TRequest).FullName}'. " +
                    "Register it with AddModuleHandlers() in the module's service registration.");

            List<IPipelineBehavior<TRequest, TResponse>> behaviours = ResolveBehaviours(serviceProvider);

            Func<Task<TResponse>> pipeline = () => handler.HandleAsync(typedRequest, cancellationToken);

            // Wrap in reverse so the first registered behaviour ends up outermost.
            for (int i = behaviours.Count - 1; i >= 0; i--)
            {
                IPipelineBehavior<TRequest, TResponse> behaviour = behaviours[i];
                Func<Task<TResponse>> next = pipeline;
                pipeline = () => behaviour.HandleAsync(typedRequest, next, cancellationToken);
            }

            return await pipeline().ConfigureAwait(false);
        }

        private static List<IPipelineBehavior<TRequest, TResponse>> ResolveBehaviours(
            IServiceProvider serviceProvider)
        {
            var behaviours = new List<IPipelineBehavior<TRequest, TResponse>>();

            object? resolved = serviceProvider.GetService(
                typeof(IEnumerable<IPipelineBehavior<TRequest, TResponse>>));

            if (resolved is not IEnumerable enumerable)
            {
                return behaviours;
            }

            foreach (object? item in enumerable)
            {
                if (item is IPipelineBehavior<TRequest, TResponse> behaviour)
                {
                    behaviours.Add(behaviour);
                }
            }

            return behaviours;
        }
    }
}

/// <summary>Helpers for discovering handler registrations in a module assembly.</summary>
public static class HandlerDiscovery
{
    private static readonly Type[] HandlerInterfaces =
    [
        typeof(IRequestHandler<,>),
    ];

    /// <summary>
    /// Finds every concrete handler in the assembly together with the closed
    /// <see cref="IRequestHandler{TRequest,TResponse}"/> interface it implements.
    /// </summary>
    /// <param name="assembly">The module assembly to scan.</param>
    /// <returns>Pairs of (service interface, implementation type) ready for DI registration.</returns>
    public static IEnumerable<(Type ServiceType, Type ImplementationType)> FindHandlers(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        foreach (Type type in assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
            {
                continue;
            }

            foreach (Type contract in type.GetInterfaces())
            {
                if (!contract.IsGenericType)
                {
                    continue;
                }

                Type definition = contract.GetGenericTypeDefinition();
                foreach (Type handlerInterface in HandlerInterfaces)
                {
                    if (definition == handlerInterface)
                    {
                        yield return (contract, type);
                    }
                }
            }
        }
    }
}
