using System.Globalization;
using AutoPartsErp.Modules.Purchasing.Domain;
using AutoPartsErp.Modules.Sales.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace AutoPartsErp.IntegrationTests.Tests;

/// <summary>
/// Order numbering in Sales and Purchasing, under the only conditions that can disprove it.
/// <para>
/// These two used to read the highest number already taken and add one, which is correct exactly
/// until two people do it in the same moment. Then both read the same highest number, both get the
/// same next one, both succeed, and nothing anywhere complains — the number was unique only
/// because of the order the reads happened to fall in. Nobody finds out until somebody looks up an
/// order and finds two.
/// </para>
/// <para>
/// A unit test cannot fail that way. There is no bug in the C#: every line of it is right, and the
/// defect exists only in the gap between two statements arriving at one database. So it is checked
/// here, the same way and for the same reason as the invoice series lock next door.
/// </para>
/// </summary>
[Collection(ErpCollection.Name)]
public sealed class OrderNumberingTests
{
    /// <summary>
    /// How many people are numbering at once.
    /// <para>
    /// Sixteen rather than two. Two collide often enough to prove the point and rarely enough to
    /// pass a few times first, which is the worst thing a concurrency test can do.
    /// </para>
    /// </summary>
    private const int Clerks = 16;

    private readonly ErpFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public OrderNumberingTests(ErpFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>Sixteen sales orders numbered at once take sixteen different numbers.</summary>
    [Fact]
    public async Task Sixteen_clerks_numbering_sales_orders_at_once_get_sixteen_different_numbers()
    {
        Guid tenant = Guid.NewGuid();

        string[] numbers = await ConcurrentlyAsync(() => NextSalesNumberAsync(tenant, 2026));

        numbers.Should().OnlyHaveUniqueItems(
            "two orders sharing a number is the failure the counter exists to prevent");

        numbers.Should().AllSatisfy(number => number.Should().StartWith("SO-2026-"));

        // Not merely distinct: consecutive from one, with nothing skipped. Sixteen unique numbers
        // that jumped around would mean the counter was being read rather than incremented, and
        // would have got away with it here by luck.
        Positions(numbers).Should().Equal(Enumerable.Range(1, Clerks).ToArray());
    }

    /// <summary>Sixteen purchase orders numbered at once take sixteen different numbers.</summary>
    [Fact]
    public async Task Sixteen_buyers_numbering_purchase_orders_at_once_get_sixteen_different_numbers()
    {
        Guid tenant = Guid.NewGuid();

        string[] numbers = await ConcurrentlyAsync(() => NextPurchaseNumberAsync(tenant, 2026));

        numbers.Should().OnlyHaveUniqueItems(
            "the supplier would otherwise receive two different orders quoting one reference");

        numbers.Should().AllSatisfy(number => number.Should().StartWith("PO-2026-"));

        Positions(numbers).Should().Equal(Enumerable.Range(1, Clerks).ToArray());
    }

    /// <summary>
    /// Each year is its own run of numbers, which is what puts the year in the number.
    /// </summary>
    [Fact]
    public async Task Each_year_starts_its_own_run()
    {
        Guid tenant = Guid.NewGuid();

        (await NextSalesNumberAsync(tenant, 2026)).Should().Be("SO-2026-00001");
        (await NextSalesNumberAsync(tenant, 2026)).Should().Be("SO-2026-00002");

        (await NextSalesNumberAsync(tenant, 2027)).Should().Be(
            "SO-2027-00001", "a new year restarts at one rather than continuing");

        // And the old year is undisturbed by the new one having been opened.
        (await NextSalesNumberAsync(tenant, 2026)).Should().Be("SO-2026-00003");
    }

    /// <summary>
    /// Two companies on one installation number independently.
    /// <para>
    /// The counter is keyed by tenant, so this is really a check that the key is the key it was
    /// declared to be. Sharing a run between tenants would be visible immediately and embarrassing
    /// permanently: one company's order numbers would have holes wherever the other one traded.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Two_tenants_number_independently()
    {
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();

        (await NextSalesNumberAsync(first, 2026)).Should().Be("SO-2026-00001");
        (await NextSalesNumberAsync(first, 2026)).Should().Be("SO-2026-00002");

        (await NextSalesNumberAsync(second, 2026)).Should().Be(
            "SO-2026-00001", "another company's trading cannot advance this one's numbering");
    }

    /// <summary>
    /// Sales and Purchasing keep separate runs, in separate schemas.
    /// <para>
    /// Both counters are called <c>number_sequences</c> and both are reached through the same
    /// method on the shared base context. If a module ever ended up writing to another module's
    /// table — the whole point of a schema per module being that it cannot — this is where it
    /// would show, as a purchase order taking a number a sales order had already used.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Sales_and_purchasing_keep_separate_runs()
    {
        Guid tenant = Guid.NewGuid();

        (await NextSalesNumberAsync(tenant, 2026)).Should().Be("SO-2026-00001");
        (await NextSalesNumberAsync(tenant, 2026)).Should().Be("SO-2026-00002");

        (await NextPurchaseNumberAsync(tenant, 2026)).Should().Be(
            "PO-2026-00001", "purchase orders are numbered in their own schema, from one");
    }

    /// <summary>The trailing counter of each number, sorted.</summary>
    private static IReadOnlyList<int> Positions(IEnumerable<string> numbers) =>
        [.. numbers
            .Select(number => int.Parse(
                number[^5..], NumberStyles.None, CultureInfo.InvariantCulture))
            .OrderBy(position => position)];

    /// <summary>
    /// Runs one call <see cref="Clerks"/> times at once and waits for all of them.
    /// <para>
    /// Started together and awaited together, each inside its own scope — which is what gives each
    /// one its own <c>DbContext</c> and its own connection. Sharing either would make them queue
    /// politely and prove nothing.
    /// </para>
    /// </summary>
    private static async Task<string[]> ConcurrentlyAsync(Func<Task<string>> take)
    {
        Task<string>[] taking =
            [.. Enumerable.Range(0, Clerks).Select(_ => Task.Run(take))];

        return await Task.WhenAll(taking);
    }

    private Task<string> NextSalesNumberAsync(Guid tenant, int year) =>
        _fixture.Application.AsTenantAsync(tenant, provider =>
            provider.GetRequiredService<ISalesOrderRepository>().NextOrderNumberAsync(year));

    private Task<string> NextPurchaseNumberAsync(Guid tenant, int year) =>
        _fixture.Application.AsTenantAsync(tenant, provider =>
            provider.GetRequiredService<IPurchaseOrderRepository>().NextOrderNumberAsync(year));
}
