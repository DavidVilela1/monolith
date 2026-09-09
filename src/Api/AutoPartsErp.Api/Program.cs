using System.Globalization;
using System.Text;
using AutoPartsErp.Api.Infrastructure;
using AutoPartsErp.IntegrationEvents.Catalog;
using AutoPartsErp.Modules.Abstractions.DependencyInjection;
using AutoPartsErp.Modules.Abstractions.Http;
using AutoPartsErp.Modules.Abstractions.Modules;
using AutoPartsErp.Modules.Access.Infrastructure.Persistence;
using AutoPartsErp.Modules.Access.Infrastructure.Persistence.Seed;
using AutoPartsErp.Modules.Access.Infrastructure.Security;
using AutoPartsErp.Modules.Access.Presentation;
using AutoPartsErp.Modules.Catalog.Infrastructure.Persistence;
using AutoPartsErp.Modules.Catalog.Infrastructure.Persistence.Seed;
using AutoPartsErp.Modules.Catalog.Presentation;
using AutoPartsErp.Modules.Finance.Infrastructure.Persistence;
using AutoPartsErp.Modules.Finance.Presentation;
using AutoPartsErp.Modules.Inventory.Infrastructure.Persistence;
using AutoPartsErp.Modules.Inventory.Infrastructure.Persistence.Seed;
using AutoPartsErp.Modules.Inventory.Presentation;
using AutoPartsErp.Modules.Invoicing.Infrastructure.Persistence;
using AutoPartsErp.Modules.Invoicing.Presentation;
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
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
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

    // Order is the contract here. The database handler is narrow and returns false for anything
    // it does not recognize; the global one handles everything and must therefore be last.
    builder.Services.AddExceptionHandler<DatabaseExceptionHandler>();
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

    // ---------------------------------------------------------------------------------
    // Who is calling, and what they may do
    //
    // The signing key is read here as well as inside the Access module, and deliberately not
    // shared through a service: validation has to be configured before the container is built,
    // and a deployment where the issuer and the validator disagree about the key is one where
    // every token this system mints is rejected by this system.
    // ---------------------------------------------------------------------------------
    AccessOptions access = builder.Configuration
        .GetSection(AccessOptions.SectionName)
        .Get<AccessOptions>() ?? new AccessOptions();

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = access.Issuer,
                ValidateAudience = true,
                ValidAudience = access.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(
                        string.IsNullOrWhiteSpace(access.SigningKey)
                            ? new string('0', 32)
                            : access.SigningKey)),

                // Zero, not the default five minutes. That default exists for clocks that drift
                // between separate machines; here the issuer and the validator are the same
                // process, and it would silently add five minutes to the life of every token -
                // to a lifetime deliberately set at fifteen.
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
            };
        });

    // Fail closed. Every endpoint requires an authenticated caller unless it says otherwise, so
    // a route added later without a thought about who may call it is refused rather than open.
    // The three that say otherwise are sign-in, refresh and sign-out, which cannot require a
    // token because they are where one comes from.
    builder.Services.AddAuthorization(options =>
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build());

    builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

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

        // So the Swagger page can be used at all now that everything needs a token: sign in
        // through /api/access/sign-in, paste the access token here, and the rest of the page
        // works. Without it every "Try it out" returns 401 and the documentation becomes
        // something to read rather than something to use.
        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Paste the access token from /api/access/sign-in. No 'Bearer ' prefix.",
        });

        options.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer",
                },
            }] = [],
        });
    });

    builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
        .WithOrigins(builder.Configuration.GetSection("Erp:AllowedOrigins").Get<string[]>() ?? [])
        .AllowAnyHeader()
        .AllowAnyMethod()));

    builder.Services.AddHealthChecks()
        .AddDbContextCheck<AccessDbContext>("access-database")
        .AddDbContextCheck<CatalogDbContext>("catalog-database")
        .AddDbContextCheck<InventoryDbContext>("inventory-database")
        .AddDbContextCheck<PartnersDbContext>("partners-database")
        .AddDbContextCheck<PricingDbContext>("pricing-database")
        .AddDbContextCheck<PurchasingDbContext>("purchasing-database")
        .AddDbContextCheck<SalesDbContext>("sales-database")
        .AddDbContextCheck<InvoicingDbContext>("invoicing-database")
        .AddDbContextCheck<FinanceDbContext>("finance-database");

    // ---------------------------------------------------------------------------------
    // Modules
    //
    // This list IS the deployment. Adding Finance later means adding one line here and
    // referencing that module's Presentation project.
    // ---------------------------------------------------------------------------------
    builder.Services.AddErpModules(
        builder.Configuration,
        new AccessModule(),
        new PartnersModule(),
        new InventoryModule(),
        new CatalogModule(),
        new PricingModule(),
        new PurchasingModule(),
        new SalesModule(),
        new InvoicingModule(),
        new FinanceModule());

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

    // Order matters and is not interchangeable: authentication works out who the caller is,
    // authorization decides whether they may proceed, and swapping them means deciding before
    // knowing - which the framework answers by refusing everybody.
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapHealthChecks("/health").AllowAnonymous();

    app.MapGet("/", (IModuleRegistry registry) => Results.Ok(new
    {
        service = "AutoParts ERP",
        version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.1.0",
        modules = registry.Modules.Select(module => new { module.Name, module.SchemaName }),
        docs = "/swagger",
    }))
    .WithName("Root")
    .AllowAnonymous()
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

    // Access first, and it is the one module whose seeding is not a development convenience.
    // A database with no users is a system nobody can sign in to, and the screen that would
    // create the first user is behind the sign-in.
    var accessContext = scope.ServiceProvider.GetRequiredService<AccessDbContext>();
    await accessContext.Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<AccessSeeder>().SeedAsync();

    // Partners has no dependencies, so it goes next and simply gets out of the way.
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

    // Invoicing last, and emphatically with no seeder. A document series has to be declared to
    // the tax authority and given a validation code before it can issue anything, so a seeded
    // one would be a series that exists here and nowhere in the AT's records - which is worse
    // than no series at all, because it looks ready.
    var invoicing = scope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
    await invoicing.Database.MigrateAsync();

    // Finance after Invoicing, and with no seeder. Its three tables are filled by events: an
    // issued document raises an open item, and a customer's terms arrive when Partners grants the
    // customer role. A seeded balance would be money the company is owed by nobody.
    var finance = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
    await finance.Database.MigrateAsync();
}

/// <summary>Exposed so integration tests can reference the host with <c>WebApplicationFactory</c>.</summary>
public partial class Program;
