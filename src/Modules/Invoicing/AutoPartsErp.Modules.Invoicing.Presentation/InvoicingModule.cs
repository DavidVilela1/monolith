using AutoPartsErp.Modules.Abstractions.DependencyInjection;
using AutoPartsErp.Modules.Abstractions.Modules;
using AutoPartsErp.Modules.Invoicing.Application.Abstractions;
using AutoPartsErp.Modules.Invoicing.Application.Options;
using AutoPartsErp.Modules.Invoicing.Domain;
using AutoPartsErp.Modules.Invoicing.Domain.Invoices;
using AutoPartsErp.Modules.Invoicing.Domain.Signing;
using AutoPartsErp.Modules.Invoicing.Infrastructure.Persistence;
using AutoPartsErp.Modules.Invoicing.Infrastructure.Persistence.ReadStore;
using AutoPartsErp.Modules.Invoicing.Infrastructure.Persistence.Repositories;
using AutoPartsErp.Modules.Invoicing.Infrastructure.Signing;
using AutoPartsErp.Modules.Invoicing.Presentation.Endpoints;
using AutoPartsErp.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AutoPartsErp.Modules.Invoicing.Presentation;

/// <summary>
/// The Invoicing module's entry point.
/// <para>
/// <see cref="Order"/> is 25, after Sales' 20. An invoice is what a sale becomes, so it registers
/// after the module that produces one — and after Catalog, whose directory it asks for the SKU and
/// description it snapshots onto every line.
/// </para>
/// </summary>
public sealed class InvoicingModule : IModule
{
    /// <inheritdoc />
    public string Name => "Invoicing";

    /// <inheritdoc />
    public string SchemaName => InvoicingDbContext.SchemaName;

    /// <inheritdoc />
    public int Order => 25;

    /// <inheritdoc />
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        string connectionString = configuration.GetConnectionString("Erp")
            ?? throw new InvalidOperationException("Connection string 'Erp' is not configured.");

        // Validated at startup, because both of these fail in ways nobody notices until an
        // inspection. A missing NIF produces a QR code whose field A is empty, which scans
        // perfectly and validates as nothing; an unknown region produces the wrong VAT rates on
        // every document a Madeira branch ever issues. Refusing to start is the kind outcome.
        services.AddOptions<InvoicingOptions>()
            .Bind(configuration.GetSection(InvoicingOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.IssuerTaxNumber),
                $"{InvoicingOptions.SectionName}:IssuerTaxNumber is required. It is field A of "
                + "every QR code this system prints.")
            .Validate(
                options => options.TaxRegion != TaxRegion.Unknown,
                $"{InvoicingOptions.SectionName}:TaxRegion must be Mainland, Azores or Madeira.")
            .ValidateOnStart();

        // The bound value, registered as itself, so the Application layer can take it without
        // referencing Microsoft.Extensions.Options. Resolving it here rather than in a handler
        // keeps the validation above in force: this factory runs through the same options
        // pipeline, so a deployment with a missing NIF still fails at startup.
        services.AddSingleton(provider =>
            provider.GetRequiredService<IOptions<InvoicingOptions>>().Value);

        services.AddScoped<AuditingInterceptor>();

        services.AddDbContext<InvoicingDbContext>((provider, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__migrations_history", InvoicingDbContext.SchemaName);

                // The only module without EnableRetryOnFailure, and for two reasons that both
                // point the same way.
                //
                // The mechanical one: a retrying execution strategy refuses to run a
                // user-initiated transaction at all, and issuing a document is the one operation
                // in this system that needs one. Turning both on means every issue throws.
                //
                // The one that matters: a retry re-runs the operation, and the aggregate it
                // re-runs against has already been mutated in memory by the attempt that failed.
                // The second pass would find a document that thinks it has been issued, having
                // committed nothing — or, worse in a slightly different failure, take a second
                // number for the same document. A transient fault here should surface as a failed
                // request that somebody repeats deliberately, not as an automatic replay of the
                // one operation whose whole point is that it happens exactly once.

                // A document has lines, and each line owns a quantity, a price and a VAT rate.
                // One join would multiply those together; a split query keeps the collection in
                // its own SELECT.
                npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            });

            options.UseSnakeCaseNamingConvention();
            options.AddInterceptors(provider.GetRequiredService<AuditingInterceptor>());
        });

        services.AddModuleOutbox<InvoicingDbContext>();

        services.AddScoped<IInvoicingUnitOfWork>(provider =>
            provider.GetRequiredService<InvoicingDbContext>());

        services.AddScoped<IDocumentSeriesRepository, DocumentSeriesRepository>();
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<IInvoicingReadStore, InvoicingReadStore>();

        // A singleton, unlike everything else here. The key is loaded once and held for the life
        // of the process: it never changes for a certified version, and parsing a PEM on every
        // request to arrive at the same key would be work done for nothing on the hot path of
        // every sale.
        services.AddSingleton<IDocumentSigner, RsaDocumentSigner>();

        services.AddModuleHandlers(
            typeof(Application.Documents.Commands.CreateDocumentCommand).Assembly);
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints
            .MapGroup("/invoicing")
            .WithTags("Invoicing");

        new DocumentSeriesEndpoints().Map(group);
        new DocumentEndpoints().Map(group);
    }
}
