using AutoPartsErp.Modules.Finance.Domain.Payments;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Finance.Domain.Payables;

/// <summary>One of the supplier's documents, and how much of the payment goes against it.</summary>
/// <param name="Item">The item being settled.</param>
/// <param name="Amount">How much goes against it.</param>
public sealed record PayableSettlementLine(PayableItem Item, Money Amount);

/// <summary>
/// Matches money that left against the supplier documents it paid.
/// <para>
/// A domain service rather than a method on either aggregate, for the reason its sibling on the
/// receivables side gives: settling is the one operation here that is not about a single
/// aggregate. It changes a payment and it changes every item that payment settled, and both
/// changes are the same fact. Both sides expose their half as <c>internal</c>, so nothing else can
/// do half the job.
/// </para>
/// <para>
/// Everything is checked before anything is changed. A four-line remittance that failed on the
/// fourth would otherwise leave three settled and the payment half spent, in memory, with nothing
/// to roll back — because nothing has touched the database yet and nothing will until the caller
/// saves. The two passes are what make it all or nothing at the level where it actually happens.
/// </para>
/// </summary>
public static class PayableSettlement
{
    /// <summary>
    /// Matches part or all of a payment against documents the company owes.
    /// </summary>
    /// <param name="payment">The money that left.</param>
    /// <param name="lines">Which documents it paid, and how much of each.</param>
    /// <param name="allocatedOn">The date of the match, normally today.</param>
    public static Result Apply(
        SupplierPayment payment,
        IReadOnlyList<PayableSettlementLine> lines,
        DateOnly allocatedOn)
    {
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentNullException.ThrowIfNull(lines);

        if (lines.Count == 0)
        {
            return FinanceErrors.Payment.NothingToAllocate;
        }

        Result shape = CheckLines(lines, payment.SupplierId, payment.Currency);

        if (shape.IsFailure)
        {
            return shape;
        }

        // A payment settles what the company owes. Matching it to a credit note would be recording
        // that the company paid a supplier for money the supplier owes back, which is not a
        // transaction that exists — a credit note is matched against an invoice, not against cash.
        foreach (PayableSettlementLine line in lines)
        {
            if (!line.Item.IsDebit)
            {
                return FinanceErrors.Payment.PaymentAgainstCredit(line.Item.DocumentNumber);
            }
        }

        Money total = Sum(lines, payment.Currency);

        if (total > payment.Unallocated)
        {
            return FinanceErrors.Payment.ExceedsUnallocated(
                payment.Number, payment.Unallocated.Amount);
        }

        // Second pass. Everything above has established that each of these succeeds, so a failure
        // here is a bug rather than a refusal — and it still returns rather than throwing, because
        // a half-applied settlement is worse than an error message.
        foreach (PayableSettlementLine line in lines)
        {
            Result settled = line.Item.Settle(line.Amount);

            if (settled.IsFailure)
            {
                return settled;
            }

            Result<SupplierPaymentAllocationId> allocated = payment.Allocate(
                line.Item.Id, line.Item.DocumentNumber, line.Amount, allocatedOn);

            if (allocated.IsFailure)
            {
                return Result.FromError(allocated.Error);
            }
        }

        return Result.Success();
    }

    private static Result CheckLines(
        IReadOnlyList<PayableSettlementLine> lines,
        SupplierRef supplierId,
        Currency currency)
    {
        var seen = new HashSet<PayableItemId>();

        foreach (PayableSettlementLine line in lines)
        {
            if (line.Item is null)
            {
                return FinanceErrors.Payment.NothingToAllocate;
            }

            // One document twice on one remittance is somebody having clicked twice, and the two
            // amounts would both be applied. Refused rather than summed: if a document really is
            // meant to take two bites of one payment, saying so as one line is unambiguous and
            // saying so as two is not.
            if (!seen.Add(line.Item.Id))
            {
                return FinanceErrors.Payment.DuplicateDocument(line.Item.DocumentNumber);
            }

            if (line.Item.SupplierId != supplierId)
            {
                return FinanceErrors.Payment.WrongSupplier(line.Item.DocumentNumber);
            }

            if (line.Item.OriginalAmount.Currency != currency)
            {
                return FinanceErrors.Payment.CurrencyMismatch;
            }

            if (!line.Item.IsOutstanding)
            {
                return FinanceErrors.Payment.NothingOwed(line.Item.DocumentNumber);
            }

            if (line.Amount > line.Item.Outstanding)
            {
                return FinanceErrors.Payable.SettlementExceedsOutstanding;
            }
        }

        return Result.Success();
    }

    private static Money Sum(IReadOnlyList<PayableSettlementLine> lines, Currency currency) =>
        lines.Aggregate(Money.Zero(currency), (running, line) => running + line.Amount);
}
