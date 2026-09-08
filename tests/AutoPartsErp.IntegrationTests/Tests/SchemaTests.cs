using AutoPartsErp.Modules.Catalog.Infrastructure.Persistence;
using AutoPartsErp.Modules.Finance.Infrastructure.Persistence;
using AutoPartsErp.Modules.Inventory.Infrastructure.Persistence;
using AutoPartsErp.Modules.Invoicing.Infrastructure.Persistence;
using AutoPartsErp.Modules.Partners.Infrastructure.Persistence;
using AutoPartsErp.Modules.Pricing.Infrastructure.Persistence;
using AutoPartsErp.Modules.Purchasing.Infrastructure.Persistence;
using AutoPartsErp.Modules.Sales.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace AutoPartsErp.IntegrationTests.Tests;

/// <summary>
/// The database the application actually builds.
/// <para>
/// This file is the reason the suite exists. Every mapping in this system is written against a
/// provider that only has an opinion at runtime — a value converter, an owned collection, a
/// filtered index predicate that is a raw SQL string EF passes through without reading. None of it
/// can fail at compile time, and until now none of it was checked until somebody ran the
/// application.
/// </para>
/// </summary>
[Collection(ErpCollection.Name)]
public sealed class SchemaTests
{
    private readonly ErpFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public SchemaTests(ErpFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Every module's migrations applied, and nothing left over.
    /// <para>
    /// A pending migration after the fixture has migrated means the migration files and the model
    /// have drifted apart — somebody changed a mapping and did not scaffold. The application would
    /// run, and the first query against the changed column would fail.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Every_module_has_applied_all_of_its_migrations()
    {
        await AssertMigratedAsync<PartnersDbContext>();
        await AssertMigratedAsync<InventoryDbContext>();
        await AssertMigratedAsync<CatalogDbContext>();
        await AssertMigratedAsync<PricingDbContext>();
        await AssertMigratedAsync<PurchasingDbContext>();
        await AssertMigratedAsync<SalesDbContext>();
        await AssertMigratedAsync<InvoicingDbContext>();
        await AssertMigratedAsync<FinanceDbContext>();
    }

    /// <summary>Each module owns its own schema, and they are all there.</summary>
    [Theory]
    [InlineData("partners")]
    [InlineData("inventory")]
    [InlineData("catalog")]
    [InlineData("pricing")]
    [InlineData("purchasing")]
    [InlineData("sales")]
    [InlineData("invoicing")]
    [InlineData("finance")]
    public async Task Each_module_created_its_own_schema(string schema)
    {
        IReadOnlyList<string> schemas = await QueryAsync(
            "SELECT nspname FROM pg_namespace WHERE nspname = @name",
            ("name", schema));

        schemas.Should().ContainSingle();
    }

    /// <summary>
    /// The filtered indexes, with the predicates they were declared with.
    /// <para>
    /// The trap this catches is written up in the README and has no other guard: the predicate is
    /// a raw SQL string naming a database column, so renaming the property without editing the
    /// string produces a migration that builds, an index that is created, and a filter that never
    /// matches anything. Everything keeps working and the uniqueness quietly stops being enforced.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("invoicing", "ux_invoices_tenant_number", "document_number <> ''")]
    [InlineData("invoicing", "ix_invoices_tenant_credited", "credited_invoice_id IS NOT NULL")]
    [InlineData("sales", "ix_sales_orders_tenant_awaiting_invoice", "invoice_id IS NULL")]
    [InlineData("pricing", "ux_price_lists_one_default_per_tenant", "is_default is_deleted")]
    [InlineData("finance", "ix_open_items_tenant_outstanding", "status")]
    [InlineData("finance", "ix_receipts_tenant_unallocated", "status")]
    public async Task A_filtered_index_exists_and_its_predicate_still_names_real_columns(
        string schema,
        string index,
        string predicateFragment)
    {
        IReadOnlyList<string> definitions = await QueryAsync(
            "SELECT indexdef FROM pg_indexes WHERE schemaname = @schema AND indexname = @index",
            ("schema", schema),
            ("index", index));

        definitions.Should().ContainSingle(
            $"{schema}.{index} should exist; if it was renamed, rename it here too");

        // PostgreSQL rewrites a predicate into its own canonical form — "= true" disappears,
        // identifiers gain quotes — so this matches on the column names rather than the text.
        // The column names are the part that can silently stop existing.
        string definition = definitions[0];
        definition.Should().Contain("WHERE");

        foreach (string column in ColumnsIn(predicateFragment))
        {
            definition.Should().Contain(column, $"the filter on {index} names {column}");
        }
    }

    /// <summary>
    /// <c>xmin</c> is the concurrency token every aggregate in this system uses.
    /// <para>
    /// It is a system column, not one a migration creates, so it is mapped rather than declared.
    /// A mapping that stopped working would take optimistic concurrency with it silently: two
    /// people would overwrite each other and neither would be told.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_concurrency_token_is_readable_on_a_real_table()
    {
        IReadOnlyList<string> rows = await QueryAsync(
            "SELECT a.attname FROM pg_attribute a "
            + "JOIN pg_class c ON c.oid = a.attrelid "
            + "JOIN pg_namespace n ON n.oid = c.relnamespace "
            + "WHERE n.nspname = 'invoicing' AND c.relname = 'invoices' AND a.attname = 'xmin'");

        rows.Should().ContainSingle();
    }

    /// <summary>Each module's outbox lives in its own schema, not in a shared one.</summary>
    [Theory]
    [InlineData("invoicing")]
    [InlineData("sales")]
    [InlineData("partners")]
    [InlineData("finance")]
    public async Task Each_module_has_its_own_outbox(string schema)
    {
        IReadOnlyList<string> tables = await QueryAsync(
            "SELECT tablename FROM pg_tables WHERE schemaname = @schema AND tablename = 'outbox_messages'",
            ("schema", schema));

        tables.Should().ContainSingle(
            "the outbox row has to be written in the same transaction as the change it describes, "
            + "which is only possible if it is in the same schema");
    }

    private static IEnumerable<string> ColumnsIn(string predicate) =>
        predicate
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Contains('_', StringComparison.Ordinal));

    private async Task AssertMigratedAsync<TContext>()
        where TContext : DbContext
    {
        await _fixture.Application.AsTenantAsync(Guid.NewGuid(), async provider =>
        {
            var context = provider.GetRequiredService<TContext>();

            IEnumerable<string> pending = await context.Database.GetPendingMigrationsAsync();

            pending.Should().BeEmpty(
                $"{typeof(TContext).Name} has migrations the fixture never applied");

            IEnumerable<string> applied = await context.Database.GetAppliedMigrationsAsync();

            applied.Should().NotBeEmpty($"{typeof(TContext).Name} has no migrations at all");
        });
    }

    private async Task<IReadOnlyList<string>> QueryAsync(
        string sql,
        params (string Name, string Value)[] parameters)
    {
        return await _fixture.Application.AsTenantAsync(Guid.NewGuid(), async provider =>
        {
            var context = provider.GetRequiredService<InvoicingDbContext>();

            await using NpgsqlConnection connection =
                new(context.Database.GetConnectionString());

            await connection.OpenAsync();

            await using NpgsqlCommand command = new(sql, connection);

            foreach ((string name, string value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            var results = new List<string>();

            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                results.Add(reader.GetString(0));
            }

            return (IReadOnlyList<string>)results;
        });
    }
}
