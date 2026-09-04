using AutoPartsErp.ModuleContracts.Pricing;
using AutoPartsErp.Modules.Abstractions.DependencyInjection;
using AutoPartsErp.Modules.Abstractions.Modules;
using AutoPartsErp.Modules.Pricing.Application.Abstractions;
using AutoPartsErp.Modules.Pricing.Domain;
using AutoPartsErp.Modules.Pricing.Infrastructure.Contracts;
using AutoPartsErp.Modules.Pricing.Infrastructure.Persistence;
using AutoPartsErp.Modules.Pricing.Infrastructure.Persistence.ReadStore;
using AutoPartsErp.Modules.Pricing.Infrastructure.Persistence.Repositories;
using AutoPartsErp.Modules.Pricing.Presentation.Endpoints;
using AutoPartsErp.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AutoPartsErp.Modules.Pricing.Presentation;

/// <summary>
/// The Pricing module's entry point.
/// <para>
/// <see cref="Order"/> is 12, between Catalog's 10 and Purchasing's 15. Pricing asks the catalogue
/// whether a part exists before it will price it, and Sales will ask Pricing what a part costs
/// before it will sell it — so it sits between the two, and the module list reads in the order
/// the business works.
/// </para>
/// </summary>
public sealed class PricingModule : IModule
{
    /// <inheritdoc />
    public string Name => "Pricing";

    /// <inheritdoc />
    public string SchemaName => PricingDbContext.SchemaName;

    /// <inheritdoc />
    public int Order => 12;

    /// <inheritdoc />
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        string connectionString = configuration.GetConnectionString("Erp")
            ?? throw new InvalidOperationException("Connection string 'Erp' is not configured.");

        services.AddScoped<AuditingInterceptor>();

        services.AddDbContext<PricingDbContext>((provider, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__migrations_history", PricingDbContext.SchemaName);
                npgsql.EnableRetryOnFailure(3);

                // No split-query default here, unlike Catalog and Sales. The only owned collection
                // in this schema is a handful of quantity breaks per price, and a second round
                // trip would cost more than the row multiplication it avoids.
            });

            options.UseSnakeCaseNamingConvention();
            options.AddInterceptors(provider.GetRequiredService<AuditingInterceptor>());
        });

        // Delivers this module's committed integration events. One sweep per module,
        // because the outbox it drains is a table in this module's own schema.
        services.AddModuleOutbox<PricingDbContext>();

        services.AddScoped<IPricingUnitOfWork>(provider =>
            provider.GetRequiredService<PricingDbContext>());

        services.AddScoped<IPriceListRepository, PriceListRepository>();
        services.AddScoped<IPriceListEntryRepository, PriceListEntryRepository>();
        services.AddScoped<ICustomerPricingRepository, CustomerPricingRepository>();
        services.AddScoped<IPriceCandidateSource, PriceCandidateSource>();
        services.AddScoped<IPricingReadStore, PricingReadStore>();

        // The question this module answers for the rest of the system, synchronously.
        services.AddScoped<IPriceProvider, PriceProvider>();

        services.AddModuleHandlers(
            typeof(Application.PriceLists.Commands.OpenPriceListCommand).Assembly);
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints
            .MapGroup("/pricing")
            .WithTags("Pricing");

        new PriceListEndpoints().Map(group);
        new QuoteEndpoints().Map(group);
    }
}
