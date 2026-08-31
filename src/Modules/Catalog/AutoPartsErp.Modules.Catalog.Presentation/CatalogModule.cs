using AutoPartsErp.Modules.Abstractions.DependencyInjection;
using AutoPartsErp.Modules.Abstractions.Modules;
using AutoPartsErp.Modules.Catalog.Application.Abstractions;
using AutoPartsErp.Modules.Catalog.Domain;
using AutoPartsErp.Modules.Catalog.Infrastructure.Persistence;
using AutoPartsErp.Modules.Catalog.Infrastructure.Persistence.ReadStore;
using AutoPartsErp.Modules.Catalog.Infrastructure.Persistence.Repositories;
using AutoPartsErp.Modules.Catalog.Infrastructure.Persistence.Seed;
using AutoPartsErp.Modules.Catalog.Presentation.Endpoints;
using AutoPartsErp.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AutoPartsErp.Modules.Catalog.Presentation;

/// <summary>
/// The Catalog module's entry point: everything the host needs to know about it.
/// <para>
/// Adding a module to a deployment is one line in <c>Program.cs</c>. Removing one is deleting
/// that line. Nothing else in the host mentions parts, brands or fitments, which is the property
/// that keeps a growing ERP from turning into a single tangled application.
/// </para>
/// </summary>
public sealed class CatalogModule : IModule
{
    /// <inheritdoc />
    public string Name => "Catalog";

    /// <inheritdoc />
    public string SchemaName => CatalogDbContext.SchemaName;

    /// <inheritdoc />
    public int Order => 10;

    /// <inheritdoc />
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        string connectionString = configuration.GetConnectionString("Erp")
            ?? throw new InvalidOperationException(
                "Connection string 'Erp' is not configured. Set ConnectionStrings:Erp in appsettings.json " +
                "or the ConnectionStrings__Erp environment variable.");

        services.AddScoped<AuditingInterceptor>();

        services.AddDbContext<CatalogDbContext>((provider, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                // Migrations live in this module's schema, so each module's history is its own.
                npgsql.MigrationsHistoryTable("__migrations_history", CatalogDbContext.SchemaName);
                npgsql.EnableRetryOnFailure(3);

                // A part has both cross-references and fitments. Loading them in one join would
                // multiply the rows together; split queries keep each collection its own SELECT.
                npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            });

            // snake_case table and column names, so the database is pleasant to query directly.
            // This replaces a hand-rolled model pass: getting it right means handling owned types
            // that share their owner's table, where the key column must stay shared rather than
            // being renamed independently.
            options.UseSnakeCaseNamingConvention();

            options.AddInterceptors(provider.GetRequiredService<AuditingInterceptor>());
        });

        services.AddScoped<ICatalogUnitOfWork>(provider => provider.GetRequiredService<CatalogDbContext>());

        services.AddScoped<IPartRepository, PartRepository>();
        services.AddScoped<IBrandRepository, BrandRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<ICatalogReadStore, CatalogReadStore>();
        services.AddScoped<CatalogSeeder>();

        // Picks up every command handler, query handler and validator in the application assembly.
        services.AddModuleHandlers(typeof(Application.Parts.Commands.CreatePartCommand).Assembly);
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints
            .MapGroup("/catalog")
            .WithTags("Catalog");

        new PartEndpoints().Map(group);
        new BrandEndpoints().Map(group);
        new CategoryEndpoints().Map(group);
    }
}
