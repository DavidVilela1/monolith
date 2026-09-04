using System.Globalization;
using AutoPartsErp.Api.Infrastructure;
using AutoPartsErp.IntegrationEvents.Catalog;
using AutoPartsErp.Modules.Abstractions.DependencyInjection;
using AutoPartsErp.Modules.Abstractions.Http;
using AutoPartsErp.Modules.Abstractions.Modules;
using AutoPartsErp.Modules.Catalog.Infrastructure.Persistence;
using AutoPartsErp.Modules.Catalog.Infrastructure.Persistence.Seed;
using AutoPartsErp.Modules.Catalog.Presentation;
using AutoPartsErp.Modules.Inventory.Infrastructure.Persistence;
using AutoPartsErp.Modules.Inventory.Infrastructure.Persistence.Seed;
using AutoPartsErp.Modules.Inventory.Presentation;
using AutoPartsErp.Modules.Partners.Infrastructure.Persistence;
using AutoPartsErp.Modules.Partners.Infrastructure.Persistence.Seed;
using AutoPartsErp.Modules.Partners.Presentation;
using AutoPartsErp.Modules.Pricing.Infrastructure.Persistence;
using AutoPartsErp.Modules.Pricing.Presentation;
using AutoPartsErp.Modules.Purchasing.Infrastructure.Persistence;
using AutoPartsErp.Modules.Purchasing.Presentation;
using AutoPartsErp.Modules.Sales.Infrastructure.Persistence;
using AutoPartsErp.Modules.Sales.Presentation;
using AutoPartsErp.Persistence;
using AutoPartsErp.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Serilog;

// Bootstrap logger: catches anything that goes wrong before configuration is even read.
// Logs are written with the invariant culture so that timestamps and numbers read the same
// whoever runs the process - a log where 1.5 becomes 1,5 on one machine is a log you cannot grep.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

try
{
    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture));

    // ---------------------------------------------------------------------------------
    // Shared services
    // ---------------------------------------------------------------------------------
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ITenantContext, HttpTenantContext>();
    builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

    // The contracts assembly is passed in so the outbox can rebuild a stored event from the
    // type name in its row. Any type from it will do; this one just has to be something that
    // will not be renamed casually.
    builder.Services.AddErpCore(typeof(PartActivatedIntegrationEvent).Assembly);
    builder.Services.AddErpPersistence(builder.Configuration);

    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "AutoParts ERP",
            Version = "v1",
            Description = "Integrated ERP for automotive parts distribution.",
        });

        foreach (string file in Directory.GetFiles(AppContext.BaseDirectory, "AutoPartsErp.*.xml"))
        {
            options.IncludeXmlComments(file, includeControllerXmlComments: true);
        }
    });

    builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
        .WithOrigins(builder.Configuration.GetSection("Erp:AllowedOrigins").Get<string[]>() ?? [])
        .AllowAnyHeader()
        .AllowAnyMethod()));

    builder.Services.AddHealthChecks()
        .AddDbContextCheck<CatalogDbContext>("catalog-database")
        .AddDbContextCheck<InventoryDbContext>("inventory-database")
        .AddDbContextCheck<PartnersDbContext>("partners-database")
        .AddDbContextCheck<PricingDbContext>("pricing-database")
        .AddDbContextCheck<PurchasingDbContext>("purchasing-database")
        .AddDbContextCheck<SalesDbContext>("sales-database");

    // ---------------------------------------------------------------------------------
    // Modules
    //
    // This list IS the deployment. Adding Finance later means adding one line here and
    // referencing that module's Presentation project.
    // ---------------------------------------------------------------------------------
    builder.Services.AddErpModules(
        builder.Configuration,
        new PartnersModule(),
        new InventoryModule(),
        new CatalogModule(),
        new PricingModule(),
        new PurchasingModule(),
        new SalesModule());

    WebApplication app = builder.Build();

    app.UseExceptionHandler();
    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "AutoParts ERP v1");
            options.DocumentTitle = "AutoParts ERP";
        });

        await MigrateAndSeedAsync(app);
    }

    app.UseCors();

    app.MapHealthChecks("/health");

    app.MapGet("/", (IModuleRegistry registry) => Results.Ok(new
    {
        service = "AutoParts ERP",
        version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.1.0",
        modules = registry.Modules.Select(module => new { module.Name, module.SchemaName }),
        docs = "/swagger",
    }))
    .WithName("Root")
    .ExcludeFromDescription();

    app.MapErpModules();

    await app.RunAsync();
    return 0;
}
// HostAbortedException is not a failure: EF Core's design-time tooling (dotnet ef migrations,
// dotnet ef database update) builds the host to read the DbContext configuration and then
// deliberately aborts it. Letting it through as FATAL buries the actual EF error underneath a
// stack trace that looks alarming and means nothing.
catch (HostAbortedException)
{
    throw;
}
catch (Exception exception)
{
    Log.Fatal(exception, "AutoParts ERP failed to start.");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

// Brings the development database up to date and seeds it.
//
// Deliberately Development-only. Applying migrations automatically on start is convenient on a
// laptop and dangerous in production, where schema changes belong in a deployment step that can
// be reviewed, timed and rolled back.
//
// A plain comment rather than an XML one: local functions are not a documentable language
// element, so /// on one is a compiler error.
static async Task MigrateAndSeedAsync(WebApplication app)
{
    using IServiceScope scope = app.Services.CreateScope();

    // Partners has no dependencies, so it goes first and simply gets out of the way.
    var partners = scope.ServiceProvider.GetRequiredService<PartnersDbContext>();
    await partners.Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<PartnersSeeder>().SeedAsync();

    // Inventory before Catalog: OpenStockRecordOnPartActivated opens a balance in every active
    // warehouse, so the warehouses have to exist before Catalog activates its seeded parts.
    var inventory = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    await inventory.Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<InventorySeeder>().SeedAsync();

    var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    await catalog.Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<CatalogSeeder>().SeedAsync();

    // Purchasing last, and with no seeder. Its two tables start empty on purpose: purchase
    // orders are raised by people, and replenishment suggestions arrive on their own the first
    // time a seeded part is picked below its reorder point.
    // Pricing has no seeder. A price list is a commercial decision, and inventing a default one
    // would be inventing what the company charges - somebody opens the first list and makes it
    // the default, and until they do, the quote endpoint says so in as many words.
    var pricing = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
    await pricing.Database.MigrateAsync();

    var purchasing = scope.ServiceProvider.GetRequiredService<PurchasingDbContext>();
    await purchasing.Database.MigrateAsync();

    // Sales last, and with no seeder either. Its customer accounts are not seeded because they
    // are not Sales' to invent: they arrive as events when Partners grants the customer role, so
    // the seeded partners populate them on the first run through the outbox.
    var sales = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
    await sales.Database.MigrateAsync();
}

/// <summary>Exposed so integration tests can reference the host with <c>WebApplicationFactory</c>.</summary>
public partial class Program;
