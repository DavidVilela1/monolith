using AutoPartsErp.ModuleContracts.Inventory;
using AutoPartsErp.Modules.Abstractions.DependencyInjection;
using AutoPartsErp.Modules.Abstractions.Modules;
using AutoPartsErp.Modules.Inventory.Application.Abstractions;
using AutoPartsErp.Modules.Inventory.Application.Counting;
using AutoPartsErp.Modules.Inventory.Application.Transfers;
using AutoPartsErp.Modules.Inventory.Domain;
using AutoPartsErp.Modules.Inventory.Infrastructure.Contracts;
using AutoPartsErp.Modules.Inventory.Infrastructure.Persistence;
using AutoPartsErp.Modules.Inventory.Infrastructure.Persistence.Jobs;
using AutoPartsErp.Modules.Inventory.Infrastructure.Persistence.ReadStore;
using AutoPartsErp.Modules.Inventory.Infrastructure.Persistence.Repositories;
using AutoPartsErp.Modules.Inventory.Infrastructure.Persistence.Seed;
using AutoPartsErp.Modules.Inventory.Presentation.Endpoints;
using AutoPartsErp.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AutoPartsErp.Modules.Inventory.Presentation;

/// <summary>
/// The Inventory module's entry point.
/// <para>
/// <see cref="Order"/> is 5, below Catalog's 10, so its warehouses are seeded before Catalog
/// activates any parts. Otherwise the handler that opens stock records would run against a
/// system with nowhere to put them.
/// </para>
/// </summary>
public sealed class InventoryModule : IModule
{
    /// <inheritdoc />
    public string Name => "Inventory";

    /// <inheritdoc />
    public string SchemaName => InventoryDbContext.SchemaName;

    /// <inheritdoc />
    public int Order => 5;

    /// <inheritdoc />
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        string connectionString = configuration.GetConnectionString("Erp")
            ?? throw new InvalidOperationException(
                "Connection string 'Erp' is not configured.");

        services.AddScoped<AuditingInterceptor>();

        services.AddDbContext<InventoryDbContext>((provider, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__migrations_history", InventoryDbContext.SchemaName);
                npgsql.EnableRetryOnFailure(3);
                npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            });

            options.UseSnakeCaseNamingConvention();
            options.AddInterceptors(provider.GetRequiredService<AuditingInterceptor>());
        });

        // Delivers this module's committed integration events. One sweep per module,
        // because the outbox it drains is a table in this module's own schema.
        services.AddModuleOutbox<InventoryDbContext>();

        services.AddScoped<IInventoryUnitOfWork>(provider =>
            provider.GetRequiredService<InventoryDbContext>());

        services.AddScoped<IStockItemRepository, StockItemRepository>();
        services.AddScoped<IStockMovementRepository, StockMovementRepository>();
        services.AddScoped<IWarehouseRepository, WarehouseRepository>();
        services.AddScoped<IStorageBinRepository, StorageBinRepository>();
        services.AddScoped<IStockCountRepository, StockCountRepository>();

        // Reads what is in a warehouse for a count sheet. Registered as the application's
        // abstraction rather than the concrete reader, because it asks Catalog a question and
        // the Application project must not know that Catalog exists.
        services.AddScoped<IStockCountScope, StockCountScope>();

        services.AddScoped<IStockTransferRepository, StockTransferRepository>();
        services.AddScoped<ITransferPartNaming, TransferPartNaming>();
        services.AddScoped<IInventoryReadStore, InventoryReadStore>();
        services.AddScoped<InventorySeeder>();

        // The one question this module answers synchronously for others. Registered here, in
        // the module that owns the data, so no consumer ever references this project — they
        // reference the contract and the container introduces them.
        services.AddScoped<IInventoryAvailability, InventoryAvailability>();

        // What stock cost, for anybody deciding whether a price is worth taking. Its own
        // interface rather than a field on availability: two questions with two audiences.
        services.AddScoped<IInventoryCosting, InventoryCosting>();

        services.AddModuleHandlers(
            typeof(Application.Stock.Commands.ReceiveStockCommand).Assembly);

        // Expiry is only a field until something acts on it.
        services.AddHostedService<ReservationSweeper>();
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints
            .MapGroup("/inventory")
            .WithTags("Inventory");

        new StockEndpoints().Map(group);
        new StockCountEndpoints().Map(group);
        new StockTransferEndpoints().Map(group);
        new WarehouseEndpoints().Map(group);
    }
}
