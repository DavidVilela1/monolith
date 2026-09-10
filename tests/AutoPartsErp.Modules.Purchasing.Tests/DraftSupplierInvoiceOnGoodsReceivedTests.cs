using AutoPartsErp.ModuleContracts.Catalog;
using AutoPartsErp.Modules.Purchasing.Application.EventHandlers;
using AutoPartsErp.Modules.Purchasing.Domain;
using AutoPartsErp.Modules.Purchasing.Domain.Agreements;
using AutoPartsErp.Modules.Purchasing.Domain.Invoices;
using AutoPartsErp.Modules.Purchasing.Domain.Orders;
using AutoPartsErp.Modules.Purchasing.Domain.Orders.Events;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Purchasing.Tests;

/// <summary>
/// The whole of automatic entry, in one handler.
/// <para>
/// A person counts a pallet; nobody types a document. These run the handler rather than the
/// aggregate because what is being protected lives in the handler: which questions get asked, what
/// happens to the answers, and what it does when one of them is missing.
/// </para>
/// </summary>
public sealed class DraftSupplierInvoiceOnGoodsReceivedTests
{
    private static readonly DateOnly Today = new(2026, 8, 12);

    /// <summary>One part, stable across a test: the agreed price and the receipt must name the same one.</summary>
    private static readonly PartRef Part = Fixture.NewPart();

    private static Money Eur(decimal amount) => Money.Of(amount, Currency.Eur);

    /// <summary>
    /// Nobody typed a line. The quantity came off the van, the price was agreed in January, and
    /// the rebate was negotiated before that.
    /// </summary>
    [Fact]
    public async Task Counting_a_pallet_writes_the_supplier_invoice()
    {
        var world = new World();
        world.AgreeRebate(4m);
        world.AgreePrice(4.50m);

        await world.Receive(quantity: 100);

        SupplierInvoice invoice = world.Invoices.Single();

        invoice.SupplierCode.Should().Be("BOSCH");
        invoice.ReceivedOn.Should().Be(Today);
        invoice.RappelRatePercent.Should().Be(4m);

        SupplierInvoiceLine line = invoice.Lines.Single();

        line.Sku.Should().Be("0986452041");
        line.Quantity.Value.Should().Be(100m);
        line.UnitPrice.Amount.Should().Be(4.50m);
        line.PriceSource.Should().NotBeNull();

        invoice.LinesTotal.Amount.Should().Be(450.00m);
        invoice.RappelAmount.Amount.Should().Be(18.00m);
    }

    /// <summary>
    /// A van is one document. Three lines off the same van land on one draft rather than opening
    /// three, because a supplier invoices a delivery and not a purchase order line.
    /// </summary>
    [Fact]
    public async Task Everything_off_the_same_van_lands_on_one_document()
    {
        var world = new World();
        world.AgreePrice(4.50m);

        await world.Receive(quantity: 100);
        await world.Receive(quantity: 20);
        await world.Receive(quantity: 5);

        world.Invoices.Should().ContainSingle();
        world.Invoices.Single().Lines.Should().HaveCount(3);
    }

    /// <summary>
    /// Goods-receipt events arrive at least once. A redelivered one must not charge the company
    /// for the same pallet twice — a mistake nobody notices until the supplier has been paid.
    /// </summary>
    [Fact]
    public async Task A_receipt_delivered_twice_is_only_charged_for_once()
    {
        var world = new World();
        world.AgreePrice(4.50m);

        GoodsReceivedDomainEvent receipt = world.NewReceipt(quantity: 100);

        await world.Handle(receipt);
        await world.Handle(receipt);

        world.Invoices.Single().Lines.Should().ContainSingle();
    }

    /// <summary>
    /// One order line legitimately arrives twice — sixty today and forty next week — and both
    /// belong on documents. What must not repeat is the same arrival, not the same line.
    /// </summary>
    [Fact]
    public async Task Two_real_deliveries_against_one_order_line_both_count()
    {
        var world = new World();
        world.AgreePrice(4.50m);

        var lineId = PurchaseOrderLineId.New();

        await world.Handle(world.NewReceipt(quantity: 60, lineId: lineId));
        await world.Handle(world.NewReceipt(quantity: 40, lineId: lineId));

        world.Invoices.Single().Lines.Should().HaveCount(2);
        world.Invoices.Single().LinesTotal.Amount.Should().Be(450.00m);
    }

    /// <summary>
    /// The goods are on the shelf. A part nobody agreed a price for is a gap in the paperwork, not
    /// a reason to reject a pallet — so the line goes on at the figure the order was placed at,
    /// and the null price source is the document saying nobody negotiated this.
    /// </summary>
    [Fact]
    public async Task A_part_with_no_agreed_price_still_reaches_the_document_and_says_so()
    {
        var world = new World();

        await world.Receive(quantity: 100, orderUnitPrice: 4.80m);

        SupplierInvoiceLine line = world.Invoices.Single().Lines.Single();

        line.UnitPrice.Amount.Should().Be(4.80m);
        line.PriceSource.Should().BeNull();
    }

    /// <summary>
    /// A price agreed from the first of October does not price a delivery that arrived in August.
    /// That is what recording a rise rather than editing one buys.
    /// </summary>
    [Fact]
    public async Task A_price_that_starts_later_does_not_price_todays_delivery()
    {
        var world = new World();
        world.AgreePrice(4.95m, from: Today.AddDays(1));

        await world.Receive(quantity: 100, orderUnitPrice: 4.50m);

        SupplierInvoiceLine line = world.Invoices.Single().Lines.Single();

        line.UnitPrice.Amount.Should().Be(4.50m);
        line.PriceSource.Should().BeNull();
    }

    /// <summary>
    /// The trap, closed here as well as in the aggregate. A rebate settled by credit note takes
    /// nothing off the document, so a draft opened at that rate would take the discount twice.
    /// </summary>
    [Fact]
    public async Task A_credit_note_rebate_leaves_the_drafted_document_alone()
    {
        var world = new World();
        world.AgreeRebateByCreditNote(4m);
        world.AgreePrice(4.50m);

        await world.Receive(quantity: 100);

        world.Invoices.Single().RappelRatePercent.Should().Be(0m);
        world.Invoices.Single().NetTotal.Amount.Should().Be(450.00m);
    }

    /// <summary>A supplier with no agreement at all is not a supplier with a rebate of nothing.</summary>
    [Fact]
    public async Task No_agreement_means_no_rebate_and_no_refusal()
    {
        var world = new World();
        world.AgreePrice(4.50m);

        await world.Receive(quantity: 100);

        world.Invoices.Single().RappelRatePercent.Should().Be(0m);
        world.Invoices.Single().Lines.Should().ContainSingle();
    }

    /// <summary>Everything one of these tests needs, wired by hand like the rest of this suite.</summary>
    private sealed class World
    {
        private readonly FakeInvoices _invoices = new();
        private readonly FakeAgreements _agreements = new();
        private readonly FakePrices _prices = new();
        private readonly PurchaseOrder _order;

        public World()
        {
            _order = PurchaseOrder.Draft(
                "PO-2026-00001", Fixture.Supplier, "BOSCH", Fixture.Warehouse, Currency.Eur).Value;
        }

        public IReadOnlyList<SupplierInvoice> Invoices => _invoices.Saved;

        public void AgreeRebate(decimal percent)
        {
            SupplierAgreement agreement = NewAgreement();
            agreement.RebateOnInvoice(RappelScale.Flat(percent, Currency.Eur).Value);
            _agreements.Agreement = agreement;
        }

        public void AgreeRebateByCreditNote(decimal percent)
        {
            SupplierAgreement agreement = NewAgreement();
            agreement.RebateByCreditNote(
                RappelScale.Flat(percent, Currency.Eur).Value, RappelPeriod.Annual);
            _agreements.Agreement = agreement;
        }

        public void AgreePrice(decimal unitPrice, DateOnly? from = null) =>
            _prices.Price = SupplierPrice.Agree(
                Fixture.Supplier, Part, Eur(unitPrice), from ?? new DateOnly(2026, 1, 1)).Value;

        public GoodsReceivedDomainEvent NewReceipt(
            int quantity,
            PurchaseOrderLineId? lineId = null,
            decimal orderUnitPrice = 4.50m) =>
            new(
                _order.Id,
                _order.OrderNumber,
                lineId ?? PurchaseOrderLineId.New(),
                Part,
                Fixture.Warehouse,
                quantity,
                UnitOfMeasure.Each.Code,
                orderUnitPrice,
                Currency.Eur.Code);

        public Task Receive(int quantity, decimal orderUnitPrice = 4.50m) =>
            Handle(NewReceipt(quantity, orderUnitPrice: orderUnitPrice));

        public Task Handle(GoodsReceivedDomainEvent receipt) =>
            new DraftSupplierInvoiceOnGoodsReceived(
                _invoices,
                _agreements,
                _prices,
                new FakeOrders(_order),
                new FakeCatalogue(),
                new FixedClock()).HandleAsync(receipt);

        private static SupplierAgreement NewAgreement() =>
            SupplierAgreement.Open(
                Fixture.Supplier, "BOSCH", Currency.Eur, new DateOnly(2026, 1, 1)).Value;
    }

    private sealed class FakeInvoices : ISupplierInvoiceRepository
    {
        private readonly List<SupplierInvoice> _saved = [];

        public IReadOnlyList<SupplierInvoice> Saved => _saved;

        public Task<SupplierInvoice?> GetByIdAsync(
            SupplierInvoiceId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_saved.Find(invoice => invoice.Id == id));

        public Task<bool> ExistsAsync(SupplierInvoiceId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_saved.Exists(invoice => invoice.Id == id));

        public Task<SupplierInvoice?> GetOpenDraftAsync(
            SupplierRef supplierId, DateOnly receivedOn, CancellationToken cancellationToken = default) =>
            Task.FromResult(_saved.Find(invoice =>
                invoice.SupplierId == supplierId
                && invoice.ReceivedOn == receivedOn
                && invoice.Status == SupplierInvoiceStatus.Drafted));

        public Task<bool> HasLineForReceiptAsync(
            PurchaseOrderLineId purchaseOrderLineId,
            decimal quantity,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_saved.Exists(invoice => invoice.Lines.Any(line =>
                line.PurchaseOrderLineId == purchaseOrderLineId && line.Quantity.Value == quantity)));

        public void Add(SupplierInvoice aggregate) => _saved.Add(aggregate);

        public void Remove(SupplierInvoice aggregate) => _saved.Remove(aggregate);
    }

    private sealed class FakeAgreements : ISupplierAgreementRepository
    {
        public SupplierAgreement? Agreement { get; set; }

        public Task<SupplierAgreement?> GetByIdAsync(
            SupplierAgreementId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Agreement);

        public Task<bool> ExistsAsync(
            SupplierAgreementId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Agreement is not null);

        public Task<SupplierAgreement?> GetForSupplierAsync(
            SupplierRef supplierId, DateOnly on, CancellationToken cancellationToken = default) =>
            Task.FromResult(Agreement);

        public Task<bool> HasLiveAgreementAsync(
            SupplierRef supplierId,
            SupplierAgreementId? excluding = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Agreement is not null);

        public void Add(SupplierAgreement aggregate) => Agreement = aggregate;

        public void Remove(SupplierAgreement aggregate) => Agreement = null;
    }

    private sealed class FakePrices : ISupplierPriceRepository
    {
        public SupplierPrice? Price { get; set; }

        public Task<SupplierPrice?> GetByIdAsync(
            SupplierPriceId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Price);

        public Task<bool> ExistsAsync(SupplierPriceId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Price is not null);

        public Task<SupplierPrice?> GetPriceOnAsync(
            SupplierRef supplierId,
            PartRef partId,
            DateOnly on,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Price is not null && Price.AppliesOn(on) ? Price : null);

        public Task<bool> ExistsForAsync(
            SupplierRef supplierId,
            PartRef partId,
            DateOnly effectiveFrom,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Price is not null && Price.EffectiveFrom == effectiveFrom);

        public void Add(SupplierPrice aggregate) => Price = aggregate;

        public void Remove(SupplierPrice aggregate) => Price = null;
    }

    private sealed class FakeOrders : IPurchaseOrderRepository
    {
        private readonly PurchaseOrder _order;

        public FakeOrders(PurchaseOrder order) => _order = order;

        public Task<PurchaseOrder?> GetByIdAsync(
            PurchaseOrderId id, CancellationToken cancellationToken = default) =>
            Task.FromResult<PurchaseOrder?>(id == _order.Id ? _order : null);

        public Task<bool> ExistsAsync(PurchaseOrderId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(id == _order.Id);

        public Task<PurchaseOrder?> GetByNumberAsync(
            string orderNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult<PurchaseOrder?>(orderNumber == _order.OrderNumber ? _order : null);

        public Task<string> NextOrderNumberAsync(int year, CancellationToken cancellationToken = default) =>
            Task.FromResult($"PO-{year}-00002");

        public Task<IReadOnlyList<PurchaseOrder>> GetOpenForSupplierAsync(
            SupplierRef supplierId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PurchaseOrder>>([]);

        public void Add(PurchaseOrder aggregate)
        {
        }

        public void Remove(PurchaseOrder aggregate)
        {
        }
    }

    private sealed class FakeCatalogue : ICatalogDirectory
    {
        private static readonly PartDescriptor Descriptor = new(
            Part.Value,
            "0986452041",
            "Oil filter",
            UnitOfMeasure.Each.Code,
            IsSellable: true,
            IsPurchasable: true,
            RequiresCoreReturn: false,
            SupersededByPartId: null);

        public Task<PartDescriptor?> GetAsync(Guid partId, CancellationToken cancellationToken = default) =>
            Task.FromResult<PartDescriptor?>(Descriptor);

        public Task<CoreCharge?> GetCoreChargeAsync(
            Guid partId, CancellationToken cancellationToken = default) =>
            Task.FromResult<CoreCharge?>(null);

        public Task<IReadOnlyDictionary<Guid, PartDescriptor>> GetManyAsync(
            IReadOnlyCollection<Guid> partIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, PartDescriptor>>(
                new Dictionary<Guid, PartDescriptor> { [Descriptor.PartId] = Descriptor });

        public Task<PartDescriptor?> FindBySkuAsync(
            string sku, CancellationToken cancellationToken = default) =>
            Task.FromResult<PartDescriptor?>(Descriptor);
    }

    private sealed class FixedClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => new(2026, 8, 12, 9, 30, 0, TimeSpan.Zero);

        public DateOnly TodayUtc => DateOnly.FromDateTime(UtcNow.UtcDateTime);
    }
}
