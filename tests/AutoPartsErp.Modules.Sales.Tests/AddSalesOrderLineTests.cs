using AutoPartsErp.ModuleContracts.Catalog;
using AutoPartsErp.ModuleContracts.Inventory;
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
            new FakeOrders(order),
            new FakeCatalogue(Describe(sellable: true)),
            pricing,
            new FakeCosting(null),
            new FakeUnitOfWork());

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

    /// <summary>
    /// The deposit comes from the catalogue, not from the caller, and it arrives as its own line.
    /// A part sold on a core without one is a starter motor the company gave the old unit away
    /// on, found weeks later when nobody brings it back and nobody was ever charged.
    /// </summary>
    [Fact]
    public async Task A_part_sold_on_a_core_arrives_with_its_deposit()
    {
        SalesOrder order = NewOrder();

        var handler = new AddSalesOrderLineCommandHandler(
            new FakeOrders(order),
            new FakeCatalogue(
                Describe(sellable: true, requiresCoreReturn: true),
                new CoreCharge(30.00m, "EUR")),
            new FakePricing(Quote(24.50m, 5m)),
            new FakeCosting(null),
            new FakeUnitOfWork());

        Result<Guid> result = await handler.HandleAsync(
            new AddSalesOrderLineCommand(order.Id.Value, PartId, Quantity: 2m));

        result.IsSuccess.Should().BeTrue();

        order.Lines.Should().HaveCount(2);

        SalesOrderLine deposit = order.CoreDepositFor(new SalesOrderLineId(result.Value))!;

        deposit.UnitPrice.Amount.Should().Be(30.00m);
        deposit.Quantity.Value.Should().Be(2m);
        deposit.DiscountPercent.Should().Be(0m);
    }

    /// <summary>
    /// A part cannot be activated with a core and no charge, so this is one somebody changed after
    /// it went live. Refused rather than sold without the deposit.
    /// </summary>
    [Fact]
    public async Task A_core_part_with_no_deposit_in_the_catalogue_is_refused()
    {
        SalesOrder order = NewOrder();

        var handler = new AddSalesOrderLineCommandHandler(
            new FakeOrders(order),
            new FakeCatalogue(Describe(sellable: true, requiresCoreReturn: true), coreCharge: null),
            new FakePricing(Quote(24.50m, 5m)),
            new FakeCosting(null),
            new FakeUnitOfWork());

        Result<Guid> result = await handler.HandleAsync(
            new AddSalesOrderLineCommand(order.Id.Value, PartId, Quantity: 2m));

        result.Error.Code.Should().Be("sales.core.charge_missing");
        order.Lines.Should().BeEmpty();
    }

    /// <summary>
    /// The cost comes from Inventory, at the shelf the goods will leave from, and it is a
    /// snapshot: what the decision was made against, not a figure that keeps moving.
    /// </summary>
    [Fact]
    public async Task A_line_records_what_the_shelf_was_worth_when_it_was_priced()
    {
        SalesOrder order = NewOrder();
        var handler = NewHandler(order, Describe(sellable: true), Quote(24.50m, 0m), unitCost: 14.00m);

        Result<Guid> result = await handler.HandleAsync(
            new AddSalesOrderLineCommand(order.Id.Value, PartId, Quantity: 4m));

        result.IsSuccess.Should().BeTrue();

        SalesOrderLine line = order.Lines.Single();

        line.UnitCost!.Amount.Should().Be(14.00m);
        line.CostOfSale!.Amount.Should().Be(56.00m);
        line.Margin!.Amount.Should().Be(42.00m);
        line.MarginPercent.Should().Be(42.86m);
    }

    /// <summary>
    /// A part Inventory cannot cost is still a part somebody can sell. It has no margin, which is
    /// a different thing from a margin of a hundred per cent, and the line says so rather than
    /// guessing.
    /// </summary>
    [Fact]
    public async Task A_part_the_shelf_cannot_cost_is_still_sold_and_has_no_margin()
    {
        SalesOrder order = NewOrder();
        var handler = NewHandler(order, Describe(sellable: true), Quote(24.50m, 0m), unitCost: null);

        Result<Guid> result = await handler.HandleAsync(
            new AddSalesOrderLineCommand(order.Id.Value, PartId, Quantity: 4m));

        result.IsSuccess.Should().BeTrue();

        SalesOrderLine line = order.Lines.Single();

        line.UnitCost.Should().BeNull();
        line.HasMargin.Should().BeFalse();
        line.Margin.Should().BeNull();
        line.MarginPercent.Should().BeNull();
        order.HasUncostedLines.Should().BeTrue();
    }

    /// <summary>
    /// The floor refuses. 24.50 against a cost of 22.00 makes 10.20 per cent, and a list that says
    /// twenty is a list that says no.
    /// </summary>
    [Fact]
    public async Task A_line_below_the_floor_is_refused_and_says_both_figures()
    {
        SalesOrder order = NewOrder();
        var handler = NewHandler(
            order, Describe(sellable: true), Quote(24.50m, 0m), unitCost: 22.00m, marginFloor: 20m);

        Result<Guid> result = await handler.HandleAsync(
            new AddSalesOrderLineCommand(order.Id.Value, PartId, Quantity: 4m));

        result.Error.Code.Should().Be("sales.line.below_margin_floor");
        result.Error.Description.Should().Contain("10.2").And.Contain("20");
        order.Lines.Should().BeEmpty();
    }

    /// <summary>
    /// The same line through the other route. The order keeps it, and keeps the fact that somebody
    /// had to authorise it — three weeks later "who let this through?" is a question with an
    /// answer.
    /// </summary>
    [Fact]
    public async Task The_override_route_takes_the_line_and_the_line_remembers()
    {
        SalesOrder order = NewOrder();
        var handler = NewHandler(
            order, Describe(sellable: true), Quote(24.50m, 0m), unitCost: 22.00m, marginFloor: 20m);

        Result<Guid> result = await handler.HandleAsync(
            new AddSalesOrderLineCommand(
                order.Id.Value, PartId, Quantity: 4m, OverrideMarginFloor: true));

        result.IsSuccess.Should().BeTrue();

        SalesOrderLine line = order.Lines.Single();

        line.MarginFloorOverridden.Should().BeTrue();
        line.MarginPercent.Should().Be(10.20m);
    }

    /// <summary>
    /// A line exactly on the floor is not below it. Getting this wrong turns every list with a
    /// round number on it into a list one cent stricter than whoever typed it meant.
    /// </summary>
    [Fact]
    public async Task A_line_exactly_on_the_floor_is_allowed()
    {
        SalesOrder order = NewOrder();

        // 20.00 sold against 16.00 of cost is 20.00% on the nose.
        var handler = NewHandler(
            order, Describe(sellable: true), Quote(20.00m, 0m), unitCost: 16.00m, marginFloor: 20m);

        Result<Guid> result = await handler.HandleAsync(
            new AddSalesOrderLineCommand(order.Id.Value, PartId, Quantity: 4m));

        result.IsSuccess.Should().BeTrue();
        order.Lines.Single().MarginFloorOverridden.Should().BeFalse();
    }

    /// <summary>
    /// The floor is compared in money, not in the rounded percentage people read. A line at
    /// 29.996 per cent displays as 30.00 and is still below a floor of 30 — and a check that
    /// compared the displayed figure would be a hole somebody eventually finds and starts using.
    /// </summary>
    [Fact]
    public async Task A_line_that_rounds_up_to_the_floor_is_still_below_it()
    {
        SalesOrder order = NewOrder();

        // 200.10 against 140.08 makes 60.02, which is 29.995002% — it displays as 30.00, and
        // thirty per cent of 200.10 is 60.03, which is a cent more than the line makes.
        var handler = NewHandler(
            order, Describe(sellable: true), Quote(200.10m, 0m), unitCost: 140.08m, marginFloor: 30m);

        Result<Guid> result = await handler.HandleAsync(
            new AddSalesOrderLineCommand(order.Id.Value, PartId, Quantity: 1m));

        result.Error.Code.Should().Be("sales.line.below_margin_floor");

        // The figure a person would have been shown, had it been let through.
        order.AddLine(
            new PartRef(PartId), "BP-1188", "Brake pad set, front axle",
            Quantity.Create(1m, UnitOfMeasure.Set).Value, Money.Of(200.10m, Currency.Eur),
            unitCost: Money.Of(140.08m, Currency.Eur)).IsSuccess.Should().BeTrue();

        order.Lines.Single().MarginPercent.Should().Be(30.00m);
    }

    /// <summary>
    /// A part Inventory cannot cost is a part the floor has nothing to say about. Refusing every
    /// one of them would stop a counter dead the first time a warehouse had never received a line.
    /// The round trip is not even made.
    /// </summary>
    [Fact]
    public async Task An_uncosted_line_is_not_measured_against_the_floor_or_asked_about()
    {
        SalesOrder order = NewOrder();
        var pricing = new FakePricing(Quote(24.50m, 0m), floor: 90m);

        var handler = new AddSalesOrderLineCommandHandler(
            new FakeOrders(order),
            new FakeCatalogue(Describe(sellable: true)),
            pricing,
            new FakeCosting(null),
            new FakeUnitOfWork());

        Result<Guid> result = await handler.HandleAsync(
            new AddSalesOrderLineCommand(order.Id.Value, PartId, Quantity: 4m));

        result.IsSuccess.Should().BeTrue();
        pricing.FloorWasAsked.Should().BeFalse();
        order.Lines.Single().MarginFloorOverridden.Should().BeFalse();
    }

    /// <summary>
    /// Nothing configured is not a floor of zero. An installation that has never heard of margin
    /// floors upgrades into this and nothing it used to sell starts being refused — including the
    /// obsolete stock it clears at a loss on purpose.
    /// </summary>
    [Fact]
    public async Task With_no_floor_anywhere_a_line_sold_under_cost_is_still_taken()
    {
        SalesOrder order = NewOrder();
        var handler = NewHandler(
            order, Describe(sellable: true), Quote(9.00m, 0m), unitCost: 14.00m, marginFloor: null);

        Result<Guid> result = await handler.HandleAsync(
            new AddSalesOrderLineCommand(order.Id.Value, PartId, Quantity: 4m));

        result.IsSuccess.Should().BeTrue();
        order.Lines.Single().Margin!.Amount.Should().Be(-20.00m);
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
            new FakeCosting(null),
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
            new FakeOrders(order),
            catalogue,
            new FakePricing(null),
            new FakeCosting(null),
            new FakeUnitOfWork());

        Result<Guid> result = await handler.HandleAsync(
            new AddSalesOrderLineCommand(order.Id.Value, PartId, Quantity: 4m));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("sales.order.not_editable");
        catalogue.WasAsked.Should().BeFalse();
    }

    private static AddSalesOrderLineCommandHandler NewHandler(
        SalesOrder order,
        PartDescriptor? descriptor,
        PartPrice? quote = null,
        decimal? unitCost = null,
        decimal? marginFloor = null) =>
        new(
            new FakeOrders(order),
            new FakeCatalogue(descriptor),
            new FakePricing(quote ?? Quote(30m, 0m), marginFloor),
            new FakeCosting(unitCost),
            new FakeUnitOfWork());

    /// <summary>
    /// Answers what a shelf is worth, or says it cannot. Null is the ordinary case in these
    /// tests, because most of them are about prices and refusals rather than margin.
    /// </summary>
    private sealed class FakeCosting : IInventoryCosting
    {
        private readonly decimal? _unitCost;

        public FakeCosting(decimal? unitCost) => _unitCost = unitCost;

        public Task<StockUnitCost?> GetUnitCostAsync(
            Guid partId,
            Guid warehouseId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_unitCost is { } cost
                ? new StockUnitCost(partId, warehouseId, cost, "EUR")
                : null);

        public Task<IReadOnlyDictionary<Guid, StockUnitCost>> GetUnitCostsAsync(
            IReadOnlyCollection<Guid> partIds,
            Guid warehouseId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, StockUnitCost>>(
                new Dictionary<Guid, StockUnitCost>());
    }

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

    private static PartDescriptor Describe(
        bool sellable,
        Guid? supersededBy = null,
        bool requiresCoreReturn = false) =>
        new(
            PartId,
            "BP-1188",
            "Brake pad set, front axle",
            UnitOfMeasure.Set.Code,
            IsSellable: sellable,
            IsPurchasable: sellable,
            requiresCoreReturn,
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
        private readonly CoreCharge? _coreCharge;

        public FakeCatalogue(PartDescriptor? descriptor, CoreCharge? coreCharge = null)
        {
            _descriptor = descriptor;
            _coreCharge = coreCharge;
        }

        public bool WasAsked { get; private set; }

        public Task<PartDescriptor?> GetAsync(Guid partId, CancellationToken cancellationToken = default)
        {
            WasAsked = true;
            return Task.FromResult(_descriptor);
        }

        public Task<CoreCharge?> GetCoreChargeAsync(
            Guid partId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_coreCharge);

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
        private readonly decimal? _floor;

        public FakePricing(PartPrice? quote, decimal? floor = null)
        {
            _quote = quote;
            _floor = floor;
        }

        public bool WasAsked { get; private set; }

        /// <summary>True once somebody asked what the line was allowed to make.</summary>
        public bool FloorWasAsked { get; private set; }

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

        public Task<decimal?> GetMinimumMarginPercentAsync(
            Guid? customerId = null,
            CancellationToken cancellationToken = default)
        {
            FloorWasAsked = true;
            return Task.FromResult(_floor);
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
