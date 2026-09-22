using AutoPartsErp.Modules.Purchasing.Domain.Orders;
using AutoPartsErp.Web.Models;

namespace AutoPartsErp.Web.Tests;

/// <summary>
/// The vocabulary of a purchase order, and the default the buyer's list opens on.
/// </summary>
public sealed class PurchaseOrderStatusTextTests
{
    /// <summary>Each state has its Portuguese words.</summary>
    [Theory]
    [InlineData("Draft", "rascunho")]
    [InlineData("Submitted", "enviada")]
    [InlineData("Confirmed", "confirmada")]
    [InlineData("PartiallyReceived", "parcial")]
    [InlineData("Received", "recebida")]
    [InlineData("ClosedShort", "fechada a menos")]
    [InlineData("Cancelled", "cancelada")]
    public void A_status_reads_in_portuguese(string status, string words)
    {
        PurchaseOrderStatusText.Of(status).Should().Be(words);
    }

    /// <summary>
    /// Every state the domain has is translated, and every one is offered as a filter.
    /// <para>
    /// The test that earns the class. A state added to the enum compiles, ships, and shows an
    /// English word on a Portuguese screen — and is missing from the filter, so the orders in it
    /// become unreachable. Nothing in the language objects to either.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_status_the_domain_has_is_translated_and_filterable()
    {
        foreach (PurchaseOrderStatus status in Enum.GetValues<PurchaseOrderStatus>())
        {
            if (status == PurchaseOrderStatus.Unknown)
            {
                continue;
            }

            string name = status.ToString();

            PurchaseOrderStatusText.Of(name).Should().NotBe(name);
            PurchaseOrderStatusText.All.Should().Contain(name);
        }
    }

    /// <summary>A word nothing recognizes is shown as it is, so it can be reported.</summary>
    [Fact]
    public void An_unrecognized_status_is_shown_as_it_is()
    {
        PurchaseOrderStatusText.Of("Frozen").Should().Be("Frozen");
    }

    /// <summary>
    /// Red is kept for the two endings a buyer has to look at twice.
    /// <para>
    /// An order in progress is work, not a fault. A screen that colours every row red stops being
    /// read, and then it cannot warn about anything.
    /// </para>
    /// </summary>
    [Fact]
    public void Only_a_bad_ending_is_red()
    {
        PurchaseOrderStatusText.Tone("Cancelled").Should().Be("tag-dg");
        PurchaseOrderStatusText.Tone("ClosedShort").Should().Be("tag-dg");
        PurchaseOrderStatusText.Tone("Received").Should().Be("tag-ok");
        PurchaseOrderStatusText.Tone("Submitted").Should().BeEmpty();
    }

    /// <summary>
    /// The list opens on what is still owed to the company.
    /// <para>
    /// A buyer asks what has not arrived, not what arrived last March. Once a company has traded
    /// for a year the second list buries the first, so the filter is on unless somebody turns it
    /// off — and turning it off has to survive being turned off.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(null, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void The_order_list_opens_on_what_is_still_owed(bool? asked, bool expected)
    {
        new OrderSearchForm { Outstanding = asked }.OutstandingOnly.Should().Be(expected);
    }
}
