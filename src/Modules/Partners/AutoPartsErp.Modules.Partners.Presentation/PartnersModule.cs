using AutoPartsErp.Modules.Abstractions.DependencyInjection;
using AutoPartsErp.Modules.Abstractions.Modules;
using AutoPartsErp.Modules.Partners.Application.Abstractions;
using AutoPartsErp.Modules.Partners.Domain;
using AutoPartsErp.Modules.Partners.Infrastructure.Persistence;
using AutoPartsErp.Modules.Partners.Infrastructure.Persistence.ReadStore;
using AutoPartsErp.Modules.Partners.Infrastructure.Persistence.Repositories;
using AutoPartsErp.Modules.Partners.Infrastructure.Persistence.Seed;
using AutoPartsErp.Modules.Partners.Presentation.Endpoints;
using AutoPartsErp.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AutoPartsErp.Modules.Partners.Presentation;

/// <summary>The Partners module's entry point.</summary>
public sealed class PartnersModule : IModule
{
    /// <inheritdoc />
    public string Name => "Partners";

    /// <inheritdoc />
    public string SchemaName => PartnersDbContext.SchemaName;

    /// <inheritdoc />
    public int Order => 1;

    /// <inheritdoc />
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        string connectionString = configuration.GetConnectionString("Erp")
            ?? throw new InvalidOperationException("Connection string 'Erp' is not configured.");

        services.AddScoped<AuditingInterceptor>();

        services.AddDbContext<PartnersDbContext>((provider, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__migrations_history", PartnersDbContext.SchemaName);
                npgsql.EnableRetryOnFailure(3);
                npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            });

            options.UseSnakeCaseNamingConvention();
            options.AddInterceptors(provider.GetRequiredService<AuditingInterceptor>());
        });

        // Delivers this module's committed integration events. One sweep per module,
        // because the outbox it drains is a table in this module's own schema.
        services.AddModuleOutbox<PartnersDbContext>();

        services.AddScoped<IPartnersUnitOfWork>(provider =>
            provider.GetRequiredService<PartnersDbContext>());

        services.AddScoped<IPartnerRepository, PartnerRepository>();
        services.AddScoped<IPartnersReadStore, PartnersReadStore>();
        services.AddScoped<PartnersSeeder>();

        services.AddModuleHandlers(
            typeof(Application.Commands.CreatePartnerCommand).Assembly);
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints
            .MapGroup("/partners")
            .WithTags("Partners");

        new PartnerEndpoints().Map(group);
    }
}
