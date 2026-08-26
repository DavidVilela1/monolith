using System.Reflection;
using AutoPartsErp.Modules.Abstractions.Behaviors;
using AutoPartsErp.Modules.Abstractions.Events;
using AutoPartsErp.Modules.Abstractions.Modules;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AutoPartsErp.Modules.Abstractions.DependencyInjection;

/// <summary>Wires the shared plumbing and composes modules into the host.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the dispatcher, the event bus, the clock and the standard pipeline behaviours.
    /// Call this once, before registering any module.
    /// </summary>
    public static IServiceCollection AddErpCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IDispatcher, Dispatcher>();
        services.TryAddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.TryAddSingleton<IEventBus, InProcessEventBus>();
        services.TryAddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

        // Order matters: logging wraps validation, which wraps the handler.
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        return services;
    }

    /// <summary>
    /// Registers every command handler, query handler and validator found in a module assembly.
    /// Modules call this from <see cref="IModule.RegisterServices"/> rather than listing handlers by hand,
    /// so adding a use case never means editing a registration file.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="assembly">The module's application assembly.</param>
    public static IServiceCollection AddModuleHandlers(this IServiceCollection services, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assembly);

        foreach ((Type serviceType, Type implementationType) in HandlerDiscovery.FindHandlers(assembly))
        {
            services.AddScoped(serviceType, implementationType);
        }

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
                if (definition == typeof(IValidator<>) ||
                    definition == typeof(IIntegrationEventHandler<>) ||
                    definition == typeof(SharedKernel.Primitives.IDomainEventHandler<>))
                {
                    services.AddScoped(contract, type);
                }
            }
        }

        return services;
    }

    /// <summary>
    /// Registers the supplied modules, in <see cref="IModule.Order"/> order, and keeps the list
    /// available for endpoint mapping and diagnostics.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration passed to each module.</param>
    /// <param name="modules">The modules that make up this deployment.</param>
    public static IServiceCollection AddErpModules(
        this IServiceCollection services,
        IConfiguration configuration,
        params IModule[] modules)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(modules);

        IModule[] ordered = [.. modules.OrderBy(m => m.Order).ThenBy(m => m.Name, StringComparer.Ordinal)];

        EnsureSchemasAreUnique(ordered);

        var registry = new ModuleRegistry(ordered);
        services.AddSingleton<IModuleRegistry>(registry);

        foreach (IModule module in ordered)
        {
            module.RegisterServices(services, configuration);
        }

        return services;
    }

    /// <summary>
    /// Maps every registered module's endpoints beneath <c>/api</c>.
    /// </summary>
    /// <param name="app">The web application.</param>
    public static IEndpointRouteBuilder MapErpModules(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        IModuleRegistry registry = app.Services.GetRequiredService<IModuleRegistry>();
        RouteGroupBuilder api = app.MapGroup("/api");

        foreach (IModule module in registry.Modules)
        {
            module.MapEndpoints(api);
        }

        return app;
    }

    private static void EnsureSchemasAreUnique(IReadOnlyList<IModule> modules)
    {
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (IModule module in modules)
        {
            if (seen.TryGetValue(module.SchemaName, out string? owner))
            {
                throw new InvalidOperationException(
                    $"Modules '{owner}' and '{module.Name}' both claim the database schema " +
                    $"'{module.SchemaName}'. Each module must own exactly one schema.");
            }

            seen[module.SchemaName] = module.Name;
        }
    }
}

/// <summary>The set of modules loaded into this deployment.</summary>
public interface IModuleRegistry
{
    /// <summary>The loaded modules, in registration order.</summary>
    IReadOnlyList<IModule> Modules { get; }
}

internal sealed class ModuleRegistry : IModuleRegistry
{
    public ModuleRegistry(IReadOnlyList<IModule> modules)
    {
        Modules = modules;
    }

    public IReadOnlyList<IModule> Modules { get; }
}
