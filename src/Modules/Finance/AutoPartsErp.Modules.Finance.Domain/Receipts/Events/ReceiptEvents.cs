using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Finance.Domain.Receipts.Events;

/// <summary>Money arrived from a customer.</summary>
/// <param name="ReceiptId">The receipt.</param>
/// <param name="Number">Its number.</param>
/// <param name="CustomerId">Who paid.</param>
/// <param name="Amount">How much.</param>
/// <param name="ReceivedOn">The date it arrived.</param>
/// <param name="Method">How it arrived.</param>
public sealed record ReceiptRecordedDomainEvent(
    ReceiptId ReceiptId,
    string Number,
    CustomerRef CustomerId,
    Money Amount,
    DateOnly ReceivedOn,
    ReceiptMethod Method) : DomainEvent;

/// <summary>
/// Every euro of a receipt has been matched to a document.
/// <para>
/// The interesting transition for anybody watching a sales ledger is the one into fully
/// allocated: unallocated money is work somebody still has to do, and this is what says the work
/// is finished.
/// </para>
/// </summary>
/// <param name="ReceiptId">The receipt.</param>
/// <param name="Number">Its number.</param>
/// <param name="CustomerId">Who paid.</param>
/// <param name="Amount">How much it was for.</param>
public sealed record ReceiptFullyAllocatedDomainEvent(
    ReceiptId ReceiptId,
    string Number,
    CustomerRef CustomerId,
    Money Amount) : DomainEvent;
