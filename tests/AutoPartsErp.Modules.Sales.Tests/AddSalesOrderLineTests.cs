using AutoPartsErp.ModuleContracts.Catalog;
using AutoPartsErp.ModuleContracts.Pricing;
using AutoPartsErp.Modules.Sales.Application.Orders.Commands;
using AutoPartsErp.Modules.Sales.Domain;
using AutoPartsErp.Modules.Sales.Domain.Orders;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Sales.Tests;

/// <summary>
/// Adding a line no longer trusts the caller to describe the part.
/// <para>
/// These run the handler rather than the aggregate, because the behaviour being protected lives
/// in the handler: which questions get asked, in what order, and what happens to the answers.
/// The fakes below are hand-rolled for the same reason the rest of this suite has no mocking
/// framework — three methods that return what the test set is easier to read than a chain of
/// configuration calls.
/// </para>
/// </summary>
public sealed class AddSalesOrderLineTests
{
    private static readonly Guid PartId = Guid.NewGuid();

    [Fact]
    public async Task The_line_takes_its_sku_description_and_unit_from_the_catalogue()
    {
        SalesOrder order = NewOrder();
        var handler = NewHandler(order, Describe(sellable: true));

        Result<Guid> result = await handler.HandleAsync(
            new AddSalesOrderLineCommand(order.Id.Value, PartId, Quantity: 4m));

        result.IsSuccess.Should().BeTrue();

        SalesOrderLine line = order.Lines.Single();
        line.Sku.Should().Be("BP-1188");
        line.Description.Should().Be("Brake pad set, front axle");
        line.Quantity.Unit.Should().Be(UnitOfMeasure.Set);
    }

    /// <summary>
    /// The gross price and the discount go onto the line separately, not the net. An invoice that
    /// shows "24.50 less 5%" is one a customer can check; one that shows 23.28 with no working is
    /// one they ring up about.
    /// </summary>
    [Fact]
    public async Task The_price_and_the_discount_come_from_pricing_and_the_list_is_recorded()
    {
        SalesOrder order = NewOrder();
        var handler = NewHandler(order, Describe(sellable: true), Quote(24.50m, 5m));

        Result<Guid> result = await handler.HandleAsync(
            new AddSalesOrderLineCommand(order.Id.Value, PartId, Quantity: 4m));

        result.IsSuccess.Should().BeTrue();

        SalesOrderLine line = order.Lines.Single();
        line.UnitPrice.Amount.Should().Be(24.50m);
        line.DiscountPercent.Should().Be(5m);
        line.NetTotal.Amount.Should().Be(93.10m);
        line.PriceSource.Should().Be("TRADE");
    }

    /// <summary>
    /// A manager knocking a tenner off is a real thing that happens several times a day. The line
    /// records that no price list was behind it, which is the honest answer to "why that figure?".
    /// </summary>
    [Fact]
    public async Task A_typed_price_overrides_pricing_and_is_recorded_as_having_no_list()
    {
        SalesOrder order = NewOrder();
        var pricing = new FakePricing(Quote(24.50m, 5m));
        var handler = new AddSalesOrderLineCommandHandler(
            new FakeOrders(order), new FakeCatalogue(Describe(sellable: true)), pricing, new FakeUnitOfWork());

        Result<Guid> result = await handler.HandleAsync(
            new AddSalesOrderLineCommand(order.Id.Value, PartId, Quantity: 4m, UnitPrice: 18m));

        result.IsSuccess.Should().BeTrue();

        SalesOrderLine line = order.Lines.Single();
        line.UnitPrice.Amount.Should().Be(18m);
        line.DiscountPercent.Should().Be(0m);
        line.PriceSource.Should().BeNull();
        pricing.WasAsked.Should().BeFalse();
    }

    [Fact]
    public async Task A_typed_discount_replaces_the_customers_agreed_one()
    {
        SalesOrder order = NewOrder();
        var handler = NewHandler(order, Describe(sellable: true), Quote(24.50m, 5m));

        Result<Guid> result = await handler.HandleAsync(
            new AddSalesOrderLineCommand(order.Id.Value, PartId, Quantity: 4m, DiscountPercent: 12m));

        result.IsSuccess.Should().BeTrue();

        SalesOrderLine line = order.Lines.Single();
        line.UnitPrice.Amount.Should().Be(24.50m);
        line.DiscountPercent.Should().Be(12m);
        line.PriceSource.Should().Be("TRADE");
    }

    [Fact]
    public async Task A_part_nothing_prices_is_refused_by_name()
    {
        SalesOrder order = NewOrder();

        // Built by hand rather than through NewHandler, because "no quote at all" and "the
        // default quote" are different things and a nullable parameter with a fallback cannot
        // express the first.
        var handler = new AddSalesOrderLineCommandHandler(
            new FakeOrders(order),
            new FakeCatalogue(Describe(sellable: true)),
            new FakePricing(null),
            new FakeUnitOfWork());

        Result<Guid> result = await handler.HandleAsync(
            new AddSalesOrderLineCommand(order.Id.Value, PartId, Quantity: 4m));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("sales.line.no_price");
        result.Error.Description.Should().Contain("BP-1188");
        order.Lines.Should().BeEmpty();
    }

    /// <summary>
    /// Refused, never converted. A sales line that quietly turns dollars into euros at whatever
    /// rate somebody configured last year is where exchange-rate losses go to hide.
    /// </summary>
    [Fact]
    public async Task A_price_in_another_currency_is_refused_rather_than_converted()
    {
        SalesOrder order = NewOrder();
        var handler = NewHandler(order, Describe(sellable: true), Quote(24.50m, 0m, currency: "USD"));

        Result<Guid> result = await handler.HandleAsync(
            new AddSalesOrderLineCommand(order.Id.Value, PartId, Quantity: 4m));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("sales.line.price_currency_mismatch");
        order.Lines.Should().BeEmpty();
    }

    [Fact]
    public async Task A_part_the_catalogue_has_never_heard_of_is_refused()
    {
        SalesOrder order = NewOrder();
        var handler = NewHandler(order, descriptor: null);

        Result<Guid> result = await handler.HandleAsync(
            new AddSalesOrderLineCommand(order.Id.Value, PartId, Quantity: 4m));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("sales.line.part_not_in_catalogue");
    }

    [Fact]
    public async Task A_part_that_is_not_sellable_is_refused_and_names_its_replacement()
    {
        Guid replacement = Guid.NewGuid();
        SalesOrder order = NewOrder();
        var handler = NewHandler(order, Describe(sellable: false, supersededBy: replacement));

        Result<Guid> result = await handler.HandleAsync(
            new AddSalesOrderLineCommand(order.Id.Value, PartId, Quantity: 4m));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("sales.line.part_not_sellable");
        result.Error.Description.Should().Contain(replacement.ToString());
        order.Lines.Should().BeEmpty();
    }

    /// <summary>
    /// A confirmed order reports that it is confirmed, not something about the part. The guard
    /// order matters: run it the other way round and somebody chasing "that part is obsolete"
    /// goes to the catalogue when the real answer was on the order all along.
    /// </summary>
    [Fact]
    public async Task A_confirmed_order_reports_itself_rather_than_the_part()
    {
        SalesOrder order = NewOrder();
        order.AddLine(
            new PartRef(Guid.NewGuid()),
            "OF-2201",
            "Oil filter",
            Quantity.Create(1m, UnitOfMeasure.Each).Value,
            Money.Of(9m, Currency.Eur)).IsSuccess.Should().BeTrue();
        order.Confirm(DateOnly.FromDateTime(DateTime.UtcNow)).IsSuccess.Should().BeTrue();

        var catalogue = new FakeCatalogue(Describe(sellable: false));
        var handler = new AddSalesOrderLineCommandHandler(
            new FakeOrders(order), catalogue, new FakePricing(null), new FakeUnitOfWork());

        Result<Guid> result = await handler.HandleAsync(
            new AddSalesOrderLineCommand(order.Id.Value, PartId, Quantity: 4m));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("sales.order.not_editable");
        catalogue.WasAsked.Should().BeFalse();
    }

    private static AddSalesOrderLineCommandHandler NewHandler(
        SalesOrder order,
        PartDescriptor? descriptor,
        PartPrice? quote = null) =>
        new(
            new FakeOrders(order),
            new FakeCatalogue(descriptor),
            new FakePricing(quote ?? Quote(30m, 0m)),
            new FakeUnitOfWork());

    private static PartPrice Quote(decimal gross, decimal discountPercent, string currency = "EUR") =>
        new(
            PartId,
            4m,
            currency,
            gross,
            discountPercent,
            gross - (gross * discountPercent / 100m),
            Guid.NewGuid(),
            "TRADE",
            1m);

    private static PartDescriptor Describe(bool sellable, Guid? supersededBy = null) =>
        new(
            PartId,
            "BP-1188",
            "Brake pad set, front axle",
            UnitOfMeasure.Set.Code,
            IsSellable: sellable,
            IsPurchasable: sellable,
            RequiresCoreReturn: false,
            supersededBy);

    private static SalesOrder NewOrder() =>
        SalesOrder.Draft(
            "SO-2026-00001",
            SalesOrderKind.Order,
            new CustomerRef(Guid.NewGuid()),
            "CUS-001",
            "Garagem Central, Lda.",
            new WarehouseRef(Guid.NewGuid()),
            Currency.Eur).Value;

    private sealed class FakeCatalogue : ICatalogDirectory
    {
        private readonly PartDescriptor? _descriptor;

        public FakeCatalogue(PartDescriptor? descriptor)
        {
            _descriptor = descriptor;
        }

        public bool WasAsked { get; private set; }

        public Task<PartDescriptor?> GetAsync(Guid partId, CancellationToken cancellationToken = default)
        {
            WasAsked = true;
            return Task.FromResult(_descriptor);
        }

        public Task<IReadOnlyDictionary<Guid, PartDescriptor>> GetManyAsync(
            IReadOnlyCollection<Guid> partIds,
            CancellationToken cancellationToken = default)
        {
            WasAsked = true;
            IReadOnlyDictionary<Guid, PartDescriptor> result = _descriptor is null
                ? new Dictionary<Guid, PartDescriptor>()
                : new Dictionary<Guid, PartDescriptor> { [_descriptor.PartId] = _descriptor };
            return Task.FromResult(result);
        }

        public Task<PartDescriptor?> FindBySkuAsync(
            string sku,
            CancellationToken cancellationToken = default)
        {
            WasAsked = true;
            return Task.FromResult(_descriptor);
        }
    }

    private sealed class FakePricing : IPriceProvider
    {
        private readonly PartPrice? _quote;

        public FakePricing(PartPrice? quote)
        {
            _quote = quote;
        }

        public bool WasAsked { get; private set; }

        public Task<PartPrice?> GetAsync(
            Guid partId,
            decimal quantity,
            Guid? customerId = null,
            DateOnly? on = null,
            CancellationToken cancellationToken = default)
        {
            WasAsked = true;
            return Task.FromResult(_quote);
        }
    }

    private sealed class FakeOrders : ISalesOrderRepository
    {
        private readonly SalesOrder _order;

        public FakeOrders(SalesOrder order)
        {
            _order = order;
        }

        public Task<SalesOrder?> GetByIdAsync(
            SalesOrderId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<SalesOrder?>(id == _order.Id ? _order : null);

        public Task<SalesOrder?> GetByNumberAsync(
            string orderNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<SalesOrder?>(orderNumber == _order.OrderNumber ? _order : null);

        public Task<string> NextOrderNumberAsync(int year, CancellationToken cancellationToken = default) =>
            Task.FromResult($"SO-{year}-00002");

        public Task<IReadOnlyList<SalesOrder>> GetOpenForCustomerAsync(
            CustomerRef customerId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SalesOrder>>([]);

        public Task<bool> ExistsAsync(SalesOrderId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(id == _order.Id);

        public void Add(SalesOrder aggregate)
        {
        }

        public void Remove(SalesOrder aggregate)
        {
        }
    }

    private sealed class FakeUnitOfWork : ISalesUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(1);
    }
}
