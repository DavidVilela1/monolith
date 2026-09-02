using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutoPartsErp.SharedKernel.Messaging;

namespace AutoPartsErp.Modules.Abstractions.Events;

/// <summary>
/// Every integration event contract this deployment knows how to rebuild.
/// <para>
/// Built once at startup by scanning the contracts assembly. A stored row names its type as a
/// string, so this map is what turns "AutoPartsErp.IntegrationEvents.Inventory.StockReceived..."
/// back into something a handler can be given — and its absence is what makes a renamed contract
/// an undeliverable message rather than a silent no-op.
/// </para>
/// </summary>
public sealed class IntegrationEventContracts
{
    private readonly Dictionary<string, Type> _byName = new(StringComparer.Ordinal);

    /// <summary>Scans the supplied assemblies for integration event contracts.</summary>
    /// <param name="assemblies">Assemblies containing <see cref="IIntegrationEvent"/> implementations.</param>
    public IntegrationEventContracts(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        foreach (Assembly assembly in assemblies)
        {
            foreach (Type type in assembly.GetTypes())
            {
                if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
                {
                    continue;
                }

                if (!typeof(IIntegrationEvent).IsAssignableFrom(type))
                {
                    continue;
                }

                string? name = type.FullName;
                if (name is not null)
                {
                    _byName[name] = type;
                }
            }
        }
    }

    /// <summary>How many contracts were found. Zero means the outbox can store but never deliver.</summary>
    public int Count => _byName.Count;

    /// <summary>Finds a contract by its stored name, or null when nothing of that name is loaded.</summary>
    public Type? Find(string typeName) =>
        _byName.TryGetValue(typeName, out Type? type) ? type : null;
}

/// <summary>
/// Stores integration events as JSON.
/// <para>
/// The type name recorded alongside is the full CLR name, which makes renaming a published
/// contract a breaking change for any row still referring to it. That is not a flaw in the
/// serializer — it is the same constraint a message broker would impose, surfaced early.
/// </para>
/// </summary>
public sealed class JsonIntegrationEventSerializer : IIntegrationEventSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

        // Written for a person to read in psql, not to save bytes. An undeliverable message is
        // read by a human far more often than a delivered one is read by anything.
        WriteIndented = false,
    };

    private readonly IntegrationEventContracts _contracts;

    /// <summary>Initializes the serializer.</summary>
    public JsonIntegrationEventSerializer(IntegrationEventContracts contracts)
    {
        _contracts = contracts ?? throw new ArgumentNullException(nameof(contracts));
    }

    /// <inheritdoc />
    public string GetTypeName(IIntegrationEvent integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        Type type = integrationEvent.GetType();

        return type.FullName ?? type.Name;
    }

    /// <inheritdoc />
    public string Serialize(IIntegrationEvent integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        // The runtime type, not IIntegrationEvent: serializing through the interface would write
        // an empty object and lose the entire payload.
        return JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType(), SerializerOptions);
    }

    /// <inheritdoc />
    public IIntegrationEvent? Deserialize(string typeName, string content)
    {
        Type? type = _contracts.Find(typeName);

        return type is null
            ? null
            : JsonSerializer.Deserialize(content, type, SerializerOptions) as IIntegrationEvent;
    }
}
