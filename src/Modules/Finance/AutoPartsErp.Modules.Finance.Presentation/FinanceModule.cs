using AutoPartsErp.Modules.Abstractions.DependencyInjection;
using AutoPartsErp.Modules.Abstractions.Modules;
using AutoPartsErp.Modules.Finance.Application.Abstractions;
using AutoPartsErp.Modules.Finance.Domain;
using AutoPartsErp.Modules.Finance.Infrastructure.Persistence;
using AutoPartsErp.Modules.Finance.Infrastructure.Persistence.ReadStore;
using AutoPartsErp.Modules.Finance.Infrastructure.Persistence.Repositories;
using AutoPartsErp.Modules.Finance.Presentation.Endpoints;
using AutoPartsErp.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AutoPartsErp.Modules.Finance.Presentation;

/// <summary>
/// The Finance module's entry point.
/// <para>
/// <see cref="Order"/> is 30, after Invoicing's 25. The sales ledger is made of documents
/// Invoicing issues, so it registers after the module that produces them — and after Partners,
/// whose customer terms decide when each of those documents falls due.
/// </para>
/// </summary>
public sealed class FinanceModule : IModule
{
    /// <inheritdoc />
    public string Name => "Finance";

    /// <inheritdoc />
    public string SchemaName => FinanceDbContext.SchemaName;

    /// <inheritdoc />
    public int Order => 30;

    /// <inheritdoc />
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        string connectionString = configuration.GetConnectionString("Erp")
            ?? throw new InvalidOperationException("Connection string 'Erp' is not configured.");

        services.AddScoped<AuditingInterceptor>();

        services.AddDbContext<FinanceDbContext>((provider, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__migrations_history", FinanceDbContext.SchemaName);

                // Retries are safe here, unlike in Invoicing. Nothing in this module holds a lock
                // across two operations: a settlement is checked in memory and committed in one
                // SaveChanges, so a retried attempt starts from a reloaded aggregate rather than
                // from one a failed attempt has already mutated.
                npgsql.EnableRetryOnFailure(3);

                // A receipt has allocations. One join would repeat the receipt once per
                // allocation; a split query keeps the collection in its own SELECT.
                npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            });

            options.UseSnakeCaseNamingConvention();
            options.AddInterceptors(provider.GetRequiredService<AuditingInterceptor>());
        });

        services.AddModuleOutbox<FinanceDbContext>();

        services.AddScoped<IFinanceUnitOfWork>(provider =>
            provider.GetRequiredService<FinanceDbContext>());

        services.AddScoped<IOpenItemRepository, OpenItemRepository>();
        services.AddScoped<IReceiptRepository, ReceiptRepository>();
        services.AddScoped<ICustomerTermsRepository, CustomerTermsRepository>();
        services.AddScoped<IFinanceReadStore, FinanceReadStore>();

        services.AddModuleHandlers(
            typeof(Application.Receipts.Commands.RecordReceiptCommand).Assembly);
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints
            .MapGroup("/finance")
            .WithTags("Finance");

        new ReceivableEndpoints().Map(group);
        new ReceiptEndpoints().Map(group);
    }
}
