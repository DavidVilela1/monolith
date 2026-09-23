using AutoPartsErp.Modules.Purchasing.Application.Contracts;
using AutoPartsErp.Web.Models;

namespace AutoPartsErp.Web.Tests;

/// <summary>
/// How much of an order has actually arrived.
/// </summary>
public sealed class OrderDetailTests
{
    /// <summary>
    /// Progress is measured in money, not in lines.
    /// <para>
    /// Eleven of twelve lines received sounds like an order almost done. If the twelfth is the
    /// engine block, it is not, and a buyer who reads "92%" and stops chasing it finds out in
    /// three weeks.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(1000, 0, 1.0)]
    [InlineData(1000, 1000, 0.0)]
    [InlineData(1000, 250, 0.75)]
    public void Progress_is_measured_in_money(decimal total, decimal outstanding, decimal expected)
    {
        Order(total, outstanding).ReceivedShare.Should().Be(expected);
    }

    /// <summary>An order worth nothing is not a division by zero.</summary>
    [Fact]
    public void An_order_worth_nothing_reads_as_nothing_received()
    {
        Order(total: 0m, outstanding: 0m).ReceivedShare.Should().Be(0m);
    }

    /// <summary>
    /// The share never leaves nought and one.
    /// <para>
    /// Outstanding above the total should not happen and would mean a bug elsewhere; what it must
    /// not do is render as −40% on a screen somebody trusts.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(1000, 1400)]
    [InlineData(1000, -200)]
    public void The_share_stays_between_nothing_and_everything(decimal total, decimal outstanding)
    {
        decimal share = Order(total, outstanding).ReceivedShare;

        share.Should().BeGreaterThanOrEqualTo(0m);
        share.Should().BeLessThanOrEqualTo(1m);
    }

    private static OrderDetail Order(decimal total, decimal outstanding) =>
        new(
            new PurchaseOrderDetail
            {
                Id = Guid.NewGuid(),
                OrderNumber = "ENC/2026/00412",
                SupplierId = Guid.NewGuid(),
                SupplierCode = "BOS",
                DeliverToWarehouseId = Guid.NewGuid(),
                Status = "Confirmed",
                CurrencyCode = "EUR",
                Total = total,
                OutstandingValue = outstanding,
                IsEditable = false,
                CanReceive = true,
                Lines = [],
            },
            "ARM-01");
}
