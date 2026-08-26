using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AutoPartsErp.Modules.Abstractions.Modules;

/// <summary>
/// One business capability of the ERP: Catalog, Inventory, Purchasing, Sales, Finance.
/// <para>
/// A module owns its own database schema, its own domain model and its own endpoints.
/// The host knows nothing about any module beyond this interface, and modules know nothing
/// about each other beyond published integration events. That is what makes this a modular
/// monolith rather than a big ball of mud: the boundaries are enforced by the project graph,
/// not by good intentions.
/// </para>
/// <para>
/// When a module eventually needs to scale independently, it already has a schema, a public
/// contract and an event surface, so extracting it into its own service is a deployment
/// change rather than a rewrite.
/// </para>
/// </summary>
public interface IModule
{
    /// <summary>Human-readable module name, used in logs, health checks and Swagger grouping.</summary>
    string Name { get; }

    /// <summary>
    /// The PostgreSQL schema this module owns. No module may read or write another module's
    /// schema; cross-module data is reached through that module's public contracts.
    /// </summary>
    string SchemaName { get; }

    /// <summary>Order in which modules are registered. Lower runs first. Defaults to 100.</summary>
    int Order => 100;

    /// <summary>Registers the module's services, persistence and handlers.</summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    void RegisterServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>Maps the module's HTTP endpoints beneath the supplied route group.</summary>
    /// <param name="endpoints">The route builder, already scoped to <c>/api</c>.</param>
    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
