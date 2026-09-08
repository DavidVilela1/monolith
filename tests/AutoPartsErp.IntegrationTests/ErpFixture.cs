using System.Globalization;
using AutoPartsErp.IntegrationEvents.Catalog;
using AutoPartsErp.Modules.Abstractions.DependencyInjection;
using AutoPartsErp.Modules.Access.Infrastructure.Persistence;
using AutoPartsErp.Modules.Access.Presentation;
using AutoPartsErp.Modules.Catalog.Infrastructure.Persistence;
using AutoPartsErp.Modules.Catalog.Presentation;
using AutoPartsErp.Modules.Finance.Infrastructure.Persistence;
using AutoPartsErp.Modules.Finance.Presentation;
using AutoPartsErp.Modules.Inventory.Infrastructure.Persistence;
using AutoPartsErp.Modules.Inventory.Presentation;
using AutoPartsErp.Modules.Invoicing.Infrastructure.Persistence;
using AutoPartsErp.Modules.Invoicing.Presentation;
using AutoPartsErp.Modules.Partners.Infrastructure.Persistence;
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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace AutoPartsErp.IntegrationTests;

/// <summary>
/// The PostgreSQL 16 this project already runs on, and the application's service graph built
/// against a throwaway database on it.
/// <para>
/// <b>No container.</b> The development database is a native PostgreSQL 16 on this machine — it is
/// the first thing the README asks for — and a suite that spun up a second copy of it in Docker
/// would be adding a dependency in order to avoid using the one that is already installed.
/// </para>
/// <para>
/// What it does instead: connects to the same server the application uses, creates a database
/// named after a fresh Guid, applies every module's migrations to it, runs the tests, and drops it
/// at the end. Nothing here can touch <c>autoparts_erp</c>, and two runs at once cannot collide.
/// </para>
/// <para>
/// <b>No <c>WebApplicationFactory</c>.</b> The service graph is built here from the same three
/// calls <c>Program.cs</c> makes — <c>AddErpCore</c>, <c>AddErpPersistence</c>,
/// <c>AddErpModules</c> — rather than by starting the web host. Nothing these tests examine (a
/// value converter, an owned collection, a filtered index, a <c>FOR UPDATE</c>) can behave
/// differently because Serilog, Swagger or the HTTP pipeline are absent, and starting the host
/// would drag in the entry point, its shutdown exception and a testing package for no gain.
/// </para>
/// </summary>
public sealed class ErpFixture : IAsyncLifetime
{
    /// <summary>Overrides the server these tests create their database on.</summary>
    public const string ConnectionVariable = "ERP_TEST_CONNECTION";

    /// <summary>
    /// The development server, as <c>appsettings.json</c> describes it, minus the database name.
    /// <para>
    /// Deliberately the same credentials the application uses. A separate test login would be one
    /// more thing to keep in step, and the first symptom of it drifting out of step is a suite
    /// that fails for a reason having nothing to do with the code.
    /// </para>
    /// </summary>
    private const string DefaultServer =
        "Host=localhost;Port=5432;Username=erp;Password=erp_dev_password";

    private string _server = DefaultServer;
    private string? _databaseName;
    private ServiceProvider? _provider;

    /// <summary>The application's services, wired to this run's database.</summary>
    public ErpApplication Application =>
        _provider is null
            ? throw new InvalidOperationException("The fixture has not been initialised.")
            : new ErpApplication(_provider);

    /// <summary>
    /// The server connection, pointed explicitly at the <c>postgres</c> maintenance database.
    /// <para>
    /// Npgsql defaults the database name to the <i>username</i> when the connection string does
    /// not name one, so <c>Username=erp</c> with no <c>Database</c> goes looking for a database
    /// called <c>erp</c> and fails with <c>3D000</c>. CREATE DATABASE and DROP DATABASE both have
    /// to be issued from some other database in any case, and <c>postgres</c> is the one every
    /// server has.
    /// </para>
    /// </summary>
    private string AdminConnectionString =>
        new NpgsqlConnectionStringBuilder(_server) { Database = "postgres" }.ConnectionString;

    /// <summary>Creates the database and builds the application against it.</summary>
    public async Task InitializeAsync()
    {
        _server = Environment.GetEnvironmentVariable(ConnectionVariable) is { Length: > 0 } configured
            ? configured
            : DefaultServer;

        string name = string.Create(
            CultureInfo.InvariantCulture,
            $"autoparts_erp_tests_{Guid.NewGuid():N}");

        await CreateDatabaseAsync(name).ConfigureAwait(false);

        // Recorded only once the database exists, so that a failed create does not send teardown
        // looking for a database that was never there and double every error message.
        _databaseName = name;

        string connectionString = new NpgsqlConnectionStringBuilder(_server)
        {
            Database = name,

            // The application's own connection string turns this on, and a test that reproduces a
            // constraint violation is far more useful with the offending row in the message.
            IncludeErrorDetail = true,
        }.ConnectionString;

        _provider = BuildProvider(connectionString);

        await MigrateAsync(_provider).ConfigureAwait(false);
    }

    /// <summary>Disposes the services and drops the database they were using.</summary>
    public async Task DisposeAsync()
    {
        if (_provider is not null)
        {
            await _provider.DisposeAsync().ConfigureAwait(false);
            _provider = null;
        }

        if (_databaseName is not null)
        {
            await DropDatabaseAsync().ConfigureAwait(false);
            _databaseName = null;
        }
    }

    /// <summary>
    /// The configuration the application reads, with the test database in place of the real one.
    /// <para>
    /// In memory rather than an <c>appsettings.Test.json</c>, because a second settings file is a
    /// second place for the Invoicing options to drift, and the ones below exist only to satisfy
    /// validation that would otherwise refuse to start.
    /// </para>
    /// </summary>
    private static IConfiguration BuildConfiguration(string connectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ConnectionStrings:Erp"] = connectionString,

                ["Erp:DefaultTenantId"] = "00000000-0000-0000-0000-000000000001",
                ["Erp:DefaultTenantCode"] = "TEST",

                // Validated at registration, so a missing one refuses to start. Thirty-two
                // characters of nothing in particular: these tests never present a token, because
                // the fixture builds the container rather than the HTTP pipeline and there is no
                // authentication middleware in front of anything it calls.
                ["Erp:Access:SigningKey"] = "integration-tests-signing-key-32-chars",

                // No bootstrap administrator. Every test runs against a database of its own and
                // creates whatever it needs; an account appearing out of configuration would be a
                // row no test asked for, in the one module where an unexpected row is a way in.
                ["Erp:Access:BootstrapAdminEmail"] = string.Empty,
                ["Erp:Access:BootstrapAdminPassword"] = string.Empty,

                // Both are validated at registration and both refuse an empty value: a missing NIF
                // produces a QR code whose field A is blank, and an unknown region produces the
                // wrong VAT rates on every document. 999999990 is the tax authority's own
                // placeholder for a final consumer, which is the right kind of obviously-not-real.
                ["Erp:Invoicing:IssuerTaxNumber"] = "999999990",
                ["Erp:Invoicing:TaxRegion"] = "Mainland",
                ["Erp:Invoicing:CertificateNumber"] = "0",
                ["Erp:Invoicing:PrivateKeyVersion"] = "1",

                // Left empty on purpose. The signer generates a throwaway 1024-bit key at startup
                // when there is no PEM, which is exactly what a test wants: the whole signing and
                // chaining path runs, and nothing it produces could be mistaken for a legal
                // document.
                ["Erp:Invoicing:PrivateKeyPem"] = string.Empty,

                ["Erp:Invoicing:Company:Name"] = "AutoPecas Central, Lda.",
                ["Erp:Invoicing:Company:AddressDetail"] = "Rua das Oficinas 12",
                ["Erp:Invoicing:Company:City"] = "Lisboa",
                ["Erp:Invoicing:Company:PostalCode"] = "1000-001",

                ["Erp:Invoicing:Product:CompanyTaxId"] = "999999990",
                ["Erp:Invoicing:Product:ProductId"] = "AutoPartsErp/AutoPartsErp",
                ["Erp:Invoicing:Product:Version"] = "1.0",
            })
            .Build();

    /// <summary>
    /// Builds the container the way <c>Program.cs</c> does, minus the web host.
    /// <para>
    /// The module list is the one thing here that is written down twice. If a Finance module is
    /// added to <c>Program.cs</c> and not to this list, these tests will keep passing while
    /// covering one module less — which is why <c>SchemaTests</c> asserts on the schemas by name.
    /// </para>
    /// </summary>
    private static ServiceProvider BuildProvider(string connectionString)
    {
        IConfiguration configuration = BuildConfiguration(connectionString);

        var services = new ServiceCollection();

        services.AddSingleton(configuration);
        services.AddLogging();

        // In place of the HTTP-bound implementations the API registers. There is no request behind
        // a test, so the tenant comes from the ambient carrier the outbox sweep already uses -
        // the seam the application has for exactly this situation.
        services.AddScoped<ITenantContext, TestTenantContext>();
        services.AddScoped<ICurrentUser, TestCurrentUser>();

        services.AddErpCore(typeof(PartActivatedIntegrationEvent).Assembly);
        services.AddErpPersistence(configuration);

        services.AddErpModules(
            configuration,
            new AccessModule(),
            new PartnersModule(),
            new InventoryModule(),
            new CatalogModule(),
            new PricingModule(),
            new PurchasingModule(),
            new SalesModule(),
            new InvoicingModule(),
            new FinanceModule());

        // The same validation the real host performs in Development. A singleton that captures a
        // scoped DbContext is a bug that behaves perfectly until the second request.
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
        });
    }

    /// <summary>
    /// Applies every module's migrations, in the order <c>Program.cs</c> applies them.
    /// <para>
    /// Explicit rather than a side effect of starting the host. A broken migration then fails here,
    /// with the database's own error, instead of failing whichever test happened to run first with
    /// something further downstream.
    /// </para>
    /// </summary>
    private static async Task MigrateAsync(IServiceProvider provider)
    {
        using IServiceScope scope = provider.CreateScope();

        await MigrateAsync<AccessDbContext>(scope).ConfigureAwait(false);
        await MigrateAsync<PartnersDbContext>(scope).ConfigureAwait(false);
        await MigrateAsync<InventoryDbContext>(scope).ConfigureAwait(false);
        await MigrateAsync<CatalogDbContext>(scope).ConfigureAwait(false);
        await MigrateAsync<PricingDbContext>(scope).ConfigureAwait(false);
        await MigrateAsync<PurchasingDbContext>(scope).ConfigureAwait(false);
        await MigrateAsync<SalesDbContext>(scope).ConfigureAwait(false);
        await MigrateAsync<InvoicingDbContext>(scope).ConfigureAwait(false);
        await MigrateAsync<FinanceDbContext>(scope).ConfigureAwait(false);
    }

    private static Task MigrateAsync<TContext>(IServiceScope scope)
        where TContext : DbContext =>
        scope.ServiceProvider.GetRequiredService<TContext>().Database.MigrateAsync();

    private async Task CreateDatabaseAsync(string name)
    {
        try
        {
            await using NpgsqlConnection connection = new(AdminConnectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            // The name is generated here from a Guid and never taken from input, which is why it
            // can be interpolated: CREATE DATABASE accepts no parameters.
            await using NpgsqlCommand create = new($"CREATE DATABASE \"{name}\"", connection);
            await create.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        catch (NpgsqlException exception)
        {
            throw new InvalidOperationException(
                "These tests need the PostgreSQL 16 this project already runs on. They tried "
                + $"\"{_server}\" (database \"postgres\") and could not get there. Start the "
                + $"service, or set {ConnectionVariable} to the right server - they create a "
                + "database of their own on it and drop it afterwards, so the development "
                + "database is never touched.",
                exception);
        }
    }

    private async Task DropDatabaseAsync()
    {
        // Pooled connections to the test database outlive the provider, so the drop would be
        // refused for being in use. Clearing the pool and then terminating whatever is left is the
        // difference between a clean teardown and a server slowly filling with dead databases.
        NpgsqlConnection.ClearAllPools();

        await using NpgsqlConnection connection = new(AdminConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await using (NpgsqlCommand terminate = new(
            "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @name",
            connection))
        {
            terminate.Parameters.AddWithValue("name", _databaseName!);
            await terminate.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await using NpgsqlCommand drop = new(
            $"DROP DATABASE IF EXISTS \"{_databaseName}\"", connection);

        await drop.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// The application's services, with the one helper the tests need to reach them.
/// </summary>
public sealed class ErpApplication
{
    /// <summary>Initializes the wrapper.</summary>
    /// <param name="provider">The built container.</param>
    public ErpApplication(IServiceProvider provider)
    {
        Services = provider;
    }

    /// <summary>The root container, for the rare test that wants it directly.</summary>
    public IServiceProvider Services { get; }

    /// <summary>
    /// Runs something inside a scope with a chosen tenant, the way a request would.
    /// <para>
    /// Most of what is worth testing here lives below HTTP — a value converter, an owned
    /// collection, a row lock — and reaching it needs a scope and a tenant. Each call gets its own
    /// scope, so two of them get two <c>DbContext</c>s and two connections, which is what lets a
    /// test make them contend rather than queue behind one another.
    /// </para>
    /// </summary>
    /// <typeparam name="T">What the work returns.</typeparam>
    /// <param name="tenantId">The tenant to run as.</param>
    /// <param name="work">The work.</param>
    public async Task<T> AsTenantAsync<T>(Guid tenantId, Func<IServiceProvider, Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        using IServiceScope scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AmbientTenant>().Set(tenantId, "TEST");

        return await work(scope.ServiceProvider).ConfigureAwait(false);
    }

    /// <summary>Runs something inside a scope with a chosen tenant.</summary>
    /// <param name="tenantId">The tenant to run as.</param>
    /// <param name="work">The work.</param>
    public Task AsTenantAsync(Guid tenantId, Func<IServiceProvider, Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        return AsTenantAsync<object?>(tenantId, async provider =>
        {
            await work(provider).ConfigureAwait(false);
            return null;
        });
    }
}

/// <summary>
/// The tenant, read from the ambient carrier rather than from a request.
/// <para>
/// It throws when nothing has been set, rather than falling back to a default. A default here
/// would make every "one tenant cannot see another's rows" test pass by accident.
/// </para>
/// </summary>
internal sealed class TestTenantContext : ITenantContext
{
    private readonly AmbientTenant _ambient;

    public TestTenantContext(AmbientTenant ambient)
    {
        _ambient = ambient;
    }

    public Guid TenantId =>
        _ambient.TenantId
        ?? throw new InvalidOperationException(
            "No tenant is set on this scope. Integration tests reach the application through "
            + "ErpApplication.AsTenantAsync, which sets one.");

    public string TenantCode => _ambient.TenantCode ?? "TEST";
}

/// <summary>The identity recorded in the audit columns during a test run.</summary>
internal sealed class TestCurrentUser : ICurrentUser
{
    public string UserId => "integration-tests";

    public string UserName => "Integration Tests";

    public bool IsAuthenticated => false;
}

/// <summary>
/// Binds the fixture to every test class that asks for it, so the database is created once for
/// the whole suite rather than once per class.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ErpCollection : ICollectionFixture<ErpFixture>
{
    /// <summary>The collection name test classes reference.</summary>
    public const string Name = "erp";
}
