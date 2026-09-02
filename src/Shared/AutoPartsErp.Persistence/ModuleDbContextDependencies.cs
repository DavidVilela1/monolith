using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;

namespace AutoPartsErp.Persistence;

/// <summary>
/// Everything <see cref="ModuleDbContext"/> needs from the container, in one object.
/// <para>
/// A single parameter rather than six, so that the next piece of shared persistence plumbing can
/// be added without editing every module's context constructor. Every member is nullable and the
/// whole thing is optional, because EF's design-time tooling builds the model with no container
/// behind it — <c>dotnet ef migrations add</c> has to work without a running application.
/// </para>
/// </summary>
public sealed class ModuleDbContextDependencies
{
    /// <summary>Initializes the dependency set.</summary>
    /// <param name="tenantContext">The active tenant, used by the global query filters.</param>
    /// <param name="domainEventDispatcher">Dispatches domain events raised during the unit of work.</param>
    /// <param name="integrationEvents">Collects integration events to be written to the outbox.</param>
    /// <param name="serializer">Turns those events into rows.</param>
    /// <param name="eventScope">The message being handled, when this save is inside a handler.</param>
    /// <param name="clock">The current time.</param>
    public ModuleDbContextDependencies(
        ITenantContext? tenantContext = null,
        IDomainEventDispatcher? domainEventDispatcher = null,
        IIntegrationEventQueue? integrationEvents = null,
        IIntegrationEventSerializer? serializer = null,
        IntegrationEventScope? eventScope = null,
        IDateTimeProvider? clock = null)
    {
        TenantContext = tenantContext;
        DomainEventDispatcher = domainEventDispatcher;
        IntegrationEvents = integrationEvents;
        Serializer = serializer;
        EventScope = eventScope;
        Clock = clock;
    }

    /// <summary>The active tenant.</summary>
    public ITenantContext? TenantContext { get; }

    /// <summary>Dispatches domain events raised during the unit of work.</summary>
    public IDomainEventDispatcher? DomainEventDispatcher { get; }

    /// <summary>Collects integration events to be written to the outbox.</summary>
    public IIntegrationEventQueue? IntegrationEvents { get; }

    /// <summary>Turns integration events into rows.</summary>
    public IIntegrationEventSerializer? Serializer { get; }

    /// <summary>The message being handled, when this save is inside an event handler.</summary>
    public IntegrationEventScope? EventScope { get; }

    /// <summary>The current time.</summary>
    public IDateTimeProvider? Clock { get; }
}
