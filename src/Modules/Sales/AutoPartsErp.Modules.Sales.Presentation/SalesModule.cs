using AutoPartsErp.Modules.Abstractions.DependencyInjection;
using AutoPartsErp.Modules.Abstractions.Modules;
using AutoPartsErp.Modules.Sales.Application.Abstractions;
using AutoPartsErp.Modules.Sales.Domain;
using AutoPartsErp.Modules.Sales.Infrastructure.Persistence;
using AutoPartsErp.Modules.Sales.Infrastructure.Persistence.ReadStore;
using AutoPartsErp.Modules.Sales.Infrastructure.Persistence.Repositories;
using AutoPartsErp.Modules.Sales.Presentation.Endpoints;
using AutoPartsErp.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AutoPartsErp.Modules.Sales.Presentation;

/// <summary>
/// The Sales module's entry point.
/// <para>
/// <see cref="Order"/> is 20, last. Nothing depends on that — modules hold each other by
/// identifier and event — but the list now reads in the order the business works: who we trade
/// with, what we stock, what we hold, what we buy, what we sell.
/// </para>
/// </summary>
public sealed class SalesModule : IModule
{
    /// <inheritdoc />
    public string Name => "Sales";

    /// <inheritdoc />
    public string SchemaName => SalesDbContext.SchemaName;

    /// <inheritdoc />
    public int Order => 20;

    /// <inheritdoc />
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        string connectionString = configuration.GetConnectionString("Erp")
            ?? throw new InvalidOperationException("Connection string 'Erp' is not configured.");

        services.AddScoped<AuditingInterceptor>();

        services.AddDbContext<SalesDbContext>((provider, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__migrations_history", SalesDbContext.SchemaName);
                npgsql.EnableRetryOnFailure(3);
                npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            });

            options.UseSnakeCaseNamingConvention();
            options.AddInterceptors(provider.GetRequiredService<AuditingInterceptor>());
        });

        // Delivers this module's committed integration events. One sweep per module,
        // because the outbox it drains is a table in this module's own schema.
        services.AddModuleOutbox<SalesDbContext>();

        services.AddScoped<ISalesUnitOfWork>(provider => provider.GetRequiredService<SalesDbContext>());

        services.AddScoped<ISalesOrderRepository, SalesOrderRepository>();
        services.AddScoped<ICustomerAccountRepository, CustomerAccountRepository>();
        services.AddScoped<ISalesReadStore, SalesReadStore>();

        services.AddModuleHandlers(
            typeof(Application.Orders.Commands.CreateSalesOrderCommand).Assembly);
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints
            .MapGroup("/sales")
            .WithTags("Sales");

        new SalesOrderEndpoints().Map(group);
        new CustomerAccountEndpoints().Map(group);
    }
}
