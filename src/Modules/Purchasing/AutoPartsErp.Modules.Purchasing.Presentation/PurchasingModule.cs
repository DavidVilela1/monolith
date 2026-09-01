using AutoPartsErp.Modules.Abstractions.DependencyInjection;
using AutoPartsErp.Modules.Abstractions.Modules;
using AutoPartsErp.Modules.Purchasing.Application.Abstractions;
using AutoPartsErp.Modules.Purchasing.Domain;
using AutoPartsErp.Modules.Purchasing.Infrastructure.Persistence;
using AutoPartsErp.Modules.Purchasing.Infrastructure.Persistence.ReadStore;
using AutoPartsErp.Modules.Purchasing.Infrastructure.Persistence.Repositories;
using AutoPartsErp.Modules.Purchasing.Presentation.Endpoints;
using AutoPartsErp.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AutoPartsErp.Modules.Purchasing.Presentation;

/// <summary>
/// The Purchasing module's entry point.
/// <para>
/// <see cref="Order"/> is 15, after Catalog's 10 and Inventory's 5. Nothing in this module needs
/// them to have run first — it holds parts, warehouses and suppliers as bare identifiers — but
/// registering it last keeps the module list reading in the order the business works: who we
/// trade with, what we stock, what we sell, what we buy.
/// </para>
/// </summary>
public sealed class PurchasingModule : IModule
{
    /// <inheritdoc />
    public string Name => "Purchasing";

    /// <inheritdoc />
    public string SchemaName => PurchasingDbContext.SchemaName;

    /// <inheritdoc />
    public int Order => 15;

    /// <inheritdoc />
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        string connectionString = configuration.GetConnectionString("Erp")
            ?? throw new InvalidOperationException("Connection string 'Erp' is not configured.");

        services.AddScoped<AuditingInterceptor>();

        services.AddDbContext<PurchasingDbContext>((provider, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__migrations_history", PurchasingDbContext.SchemaName);
                npgsql.EnableRetryOnFailure(3);

                // An order has lines, and each line has three owned values of its own. Loading
                // them in one join multiplies rows together; split queries keep the collection
                // its own SELECT.
                npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            });

            options.UseSnakeCaseNamingConvention();
            options.AddInterceptors(provider.GetRequiredService<AuditingInterceptor>());
        });

        services.AddScoped<IPurchasingUnitOfWork>(provider =>
            provider.GetRequiredService<PurchasingDbContext>());

        services.AddScoped<IPurchaseOrderRepository, PurchaseOrderRepository>();
        services.AddScoped<IReplenishmentSuggestionRepository, ReplenishmentSuggestionRepository>();
        services.AddScoped<IPurchasingReadStore, PurchasingReadStore>();

        services.AddModuleHandlers(
            typeof(Application.Orders.Commands.CreatePurchaseOrderCommand).Assembly);
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints
            .MapGroup("/purchasing")
            .WithTags("Purchasing");

        new PurchaseOrderEndpoints().Map(group);
        new ReplenishmentEndpoints().Map(group);
    }
}
