using AutoPartsErp.ModuleContracts.Catalog;
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
            new AddSalesOrderLineCommand(order.Id.Value, PartId, Quantity: 4m, UnitPrice: 30m));

        result.IsSuccess.Should().BeTrue();

        SalesOrderLine line = order.Lines.Single();
        line.Sku.Should().Be("BP-1188");
        line.Description.Should().Be("Brake pad set, front axle");
        line.Quantity.Unit.Should().Be(UnitOfMeasure.Set);
    }

    [Fact]
    public async Task A_part_the_catalogue_has_never_heard_of_is_refused()
    {
        SalesOrder order = NewOrder();
        var handler = NewHandler(order, descriptor: null);

        Result<Guid> result = await handler.HandleAsync(
            new AddSalesOrderLineCommand(order.Id.Value, PartId, Quantity: 4m, UnitPrice: 30m));

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
            new AddSalesOrderLineCommand(order.Id.Value, PartId, Quantity: 4m, UnitPrice: 30m));

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
            new FakeOrders(order), catalogue, new FakeUnitOfWork());

        Result<Guid> result = await handler.HandleAsync(
            new AddSalesOrderLineCommand(order.Id.Value, PartId, Quantity: 4m, UnitPrice: 30m));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("sales.order.not_editable");
        catalogue.WasAsked.Should().BeFalse();
    }

    private static AddSalesOrderLineCommandHandler NewHandler(
        SalesOrder order,
        PartDescriptor? descriptor) =>
        new(new FakeOrders(order), new FakeCatalogue(descriptor), new FakeUnitOfWork());

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
