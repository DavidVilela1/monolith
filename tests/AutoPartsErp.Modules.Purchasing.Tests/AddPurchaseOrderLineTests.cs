using AutoPartsErp.ModuleContracts.Catalog;
using AutoPartsErp.Modules.Purchasing.Application.Orders.Commands;
using AutoPartsErp.Modules.Purchasing.Domain;
using AutoPartsErp.Modules.Purchasing.Domain.Orders;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Purchasing.Tests;

/// <summary>
/// Adding a line no longer trusts the caller to describe the part, and the rule here is stricter
/// than the one in Sales: a discontinued part may still be sold down off the shelf, but ordering
/// more of it is how dead stock gets bought on purpose.
/// </summary>
public sealed class AddPurchaseOrderLineTests
{
    private static readonly Guid PartId = Guid.NewGuid();

    [Fact]
    public async Task The_line_takes_its_sku_description_and_unit_from_the_catalogue()
    {
        PurchaseOrder order = NewOrder();
        var handler = NewHandler(order, Describe(purchasable: true));

        Result<Guid> result = await handler.HandleAsync(
            new AddPurchaseOrderLineCommand(order.Id.Value, PartId, Quantity: 12m, UnitPrice: 18m));

        result.IsSuccess.Should().BeTrue();

        PurchaseOrderLine line = order.Lines.Single();
        line.Sku.Should().Be("BP-1188");
        line.Description.Should().Be("Brake pad set, front axle");
        line.Quantity.Unit.Should().Be(UnitOfMeasure.Set);
    }

    [Fact]
    public async Task A_part_the_catalogue_has_never_heard_of_is_refused()
    {
        PurchaseOrder order = NewOrder();
        var handler = NewHandler(order, descriptor: null);

        Result<Guid> result = await handler.HandleAsync(
            new AddPurchaseOrderLineCommand(order.Id.Value, PartId, Quantity: 12m, UnitPrice: 18m));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("purchasing.line.part_not_in_catalogue");
    }

    /// <summary>
    /// The case the two modules disagree on, deliberately: the catalogue says this part may still
    /// be sold and may not be bought, and Purchasing is the one that has to refuse it.
    /// </summary>
    [Fact]
    public async Task A_part_that_is_sellable_but_not_purchasable_is_refused()
    {
        Guid replacement = Guid.NewGuid();
        PurchaseOrder order = NewOrder();
        var handler = NewHandler(
            order,
            new PartDescriptor(
                PartId,
                "BP-1188",
                "Brake pad set, front axle",
                UnitOfMeasure.Set.Code,
                IsSellable: true,
                IsPurchasable: false,
                RequiresCoreReturn: false,
                replacement));

        Result<Guid> result = await handler.HandleAsync(
            new AddPurchaseOrderLineCommand(order.Id.Value, PartId, Quantity: 12m, UnitPrice: 18m));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("purchasing.line.part_not_purchasable");
        result.Error.Description.Should().Contain(replacement.ToString());
        order.Lines.Should().BeEmpty();
    }

    [Fact]
    public async Task A_submitted_order_reports_itself_rather_than_the_part()
    {
        PurchaseOrder order = NewOrder();
        order.AddLine(
            new PartRef(Guid.NewGuid()),
            "OF-2201",
            "Oil filter",
            Quantity.Create(1m, UnitOfMeasure.Each).Value,
            Money.Of(4m, Currency.Eur)).IsSuccess.Should().BeTrue();
        order.Submit(DateOnly.FromDateTime(DateTime.UtcNow)).IsSuccess.Should().BeTrue();

        var catalogue = new FakeCatalogue(Describe(purchasable: true));
        var handler = new AddPurchaseOrderLineCommandHandler(
            new FakeOrders(order), catalogue, new FakeUnitOfWork());

        Result<Guid> result = await handler.HandleAsync(
            new AddPurchaseOrderLineCommand(order.Id.Value, PartId, Quantity: 12m, UnitPrice: 18m));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("purchasing.order.not_editable");
        catalogue.WasAsked.Should().BeFalse();
    }

    private static AddPurchaseOrderLineCommandHandler NewHandler(
        PurchaseOrder order,
        PartDescriptor? descriptor) =>
        new(new FakeOrders(order), new FakeCatalogue(descriptor), new FakeUnitOfWork());

    private static PartDescriptor Describe(bool purchasable) =>
        new(
            PartId,
            "BP-1188",
            "Brake pad set, front axle",
            UnitOfMeasure.Set.Code,
            IsSellable: true,
            IsPurchasable: purchasable,
            RequiresCoreReturn: false,
            SupersededByPartId: null);

    private static PurchaseOrder NewOrder() =>
        PurchaseOrder.Draft(
            "PO-2026-00001",
            new SupplierRef(Guid.NewGuid()),
            "SUP-001",
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

    private sealed class FakeOrders : IPurchaseOrderRepository
    {
        private readonly PurchaseOrder _order;

        public FakeOrders(PurchaseOrder order)
        {
            _order = order;
        }

        public Task<PurchaseOrder?> GetByIdAsync(
            PurchaseOrderId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<PurchaseOrder?>(id == _order.Id ? _order : null);

        public Task<PurchaseOrder?> GetByNumberAsync(
            string orderNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<PurchaseOrder?>(orderNumber == _order.OrderNumber ? _order : null);

        public Task<string> NextOrderNumberAsync(int year, CancellationToken cancellationToken = default) =>
            Task.FromResult($"PO-{year}-00002");

        public Task<IReadOnlyList<PurchaseOrder>> GetOpenForSupplierAsync(
            SupplierRef supplierId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PurchaseOrder>>([]);

        public Task<bool> ExistsAsync(PurchaseOrderId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(id == _order.Id);

        public void Add(PurchaseOrder aggregate)
        {
        }

        public void Remove(PurchaseOrder aggregate)
        {
        }
    }

    private sealed class FakeUnitOfWork : IPurchasingUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(1);
    }
}
