using AutoPartsErp.Modules.Finance.Domain.Receipts;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Finance.Domain.Receivables;

/// <summary>One document, and how much of the money or credit goes against it.</summary>
/// <param name="Item">The item being settled.</param>
/// <param name="Amount">How much goes against it.</param>
public sealed record SettlementLine(OpenItem Item, Money Amount);

/// <summary>
/// Matches money or a credit note against the documents it pays.
/// <para>
/// A domain service rather than a method on either aggregate, because settling is the one
/// operation here that is not about a single aggregate: it changes a receipt and it changes every
/// item that receipt paid, and both changes are the same fact. Putting it on <c>Receipt</c> would
/// have a receipt reaching into items; putting it on <c>OpenItem</c> would have an item reaching
/// into a receipt. Neither is true, so it lives between them, and both sides expose their half of
/// it as <c>internal</c> so that nothing else can do half the job.
/// </para>
/// <para>
/// Everything is checked before anything is changed. A four-line allocation that fails on the
/// fourth line would otherwise leave three settled and the receipt half spent, in memory, with no
/// transaction rolled back — because nothing has touched the database yet and nothing is going
/// to until the caller saves. The two passes are what make the operation all or nothing at the
/// level where it actually happens.
/// </para>
/// </summary>
public static class Settlement
{
    /// <summary>
    /// Matches part or all of a receipt against documents the customer owes.
    /// </summary>
    /// <param name="receipt">The money.</param>
    /// <param name="lines">Which documents it pays, and how much of each.</param>
    /// <param name="allocatedOn">The date of the match, normally today.</param>
    public static Result Apply(
        Receipt receipt,
        IReadOnlyList<SettlementLine> lines,
        DateOnly allocatedOn)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(lines);

        if (lines.Count == 0)
        {
            return FinanceErrors.Settlement.NothingToAllocate;
        }

        Result shape = CheckLines(lines, receipt.CustomerId, receipt.Currency);
        if (shape.IsFailure)
        {
            return shape;
        }

        // A receipt pays what the customer owes. Matching it to a credit note would be recording
        // that they paid us for money we owe them, which is not a transaction that exists.
        foreach (SettlementLine line in lines)
        {
            if (!line.Item.IsDebit)
            {
                return FinanceErrors.Settlement.ReceiptAgainstCredit(line.Item.DocumentNumber);
            }
        }

        Money total = Sum(lines, receipt.Currency);

        if (total > receipt.Unallocated)
        {
            return FinanceErrors.Settlement.ExceedsUnallocated(
                receipt.Number, receipt.Unallocated.Amount);
        }

        // Second pass. Everything above has already established that each of these succeeds, so a
        // failure here is a bug rather than a refusal - and it still returns rather than throwing,
        // because a half-applied settlement is worse than an error message.
        foreach (SettlementLine line in lines)
        {
            Result settled = line.Item.Settle(line.Amount);
            if (settled.IsFailure)
            {
                return settled;
            }

            Result<ReceiptAllocationId> allocated = receipt.Allocate(
                line.Item.Id, line.Item.DocumentNumber, line.Amount, allocatedOn);

            if (allocated.IsFailure)
            {
                return Result.Failure(allocated.Error);
            }
        }

        return Result.Success();
    }

    /// <summary>
    /// Matches a credit note against documents the customer owes.
    /// <para>
    /// The same operation as a receipt with no money in it. A credit note is worth exactly what an
    /// equivalent payment would be worth, which is why both sides of the match are the same type
    /// and the same method settles them.
    /// </para>
    /// </summary>
    /// <param name="credit">The credit note's item.</param>
    /// <param name="lines">Which documents it offsets, and how much of each.</param>
    public static Result ApplyCredit(OpenItem credit, IReadOnlyList<SettlementLine> lines)
    {
        ArgumentNullException.ThrowIfNull(credit);
        ArgumentNullException.ThrowIfNull(lines);

        if (!credit.IsCredit)
        {
            return FinanceErrors.Settlement.NotACreditNote(credit.DocumentNumber);
        }

        if (!credit.IsOutstanding)
        {
            return FinanceErrors.Settlement.CreditExhausted(credit.DocumentNumber);
        }

        if (lines.Count == 0)
        {
            return FinanceErrors.Settlement.NothingToAllocate;
        }

        Result shape = CheckLines(lines, credit.CustomerId, credit.Currency);
        if (shape.IsFailure)
        {
            return shape;
        }

        foreach (SettlementLine line in lines)
        {
            // Checked before the kind, because a credit note pointed at itself is also a credit
            // note pointed at a credit note, and the specific message is the useful one.
            if (line.Item.Id == credit.Id)
            {
                return FinanceErrors.Settlement.CreditAgainstItself(credit.DocumentNumber);
            }

            if (!line.Item.IsDebit)
            {
                return FinanceErrors.Settlement.CreditAgainstCredit(line.Item.DocumentNumber);
            }
        }

        Money total = Sum(lines, credit.Currency);

        if (total > credit.Outstanding)
        {
            return FinanceErrors.Settlement.ExceedsOutstanding(
                credit.DocumentNumber, credit.Outstanding.Amount);
        }

        foreach (SettlementLine line in lines)
        {
            Result settledDebit = line.Item.Settle(line.Amount);
            if (settledDebit.IsFailure)
            {
                return settledDebit;
            }

            // The credit is consumed by exactly what it offset, so it carries its own record of
            // how much is left without anybody having to add up the other side.
            Result settledCredit = credit.Settle(line.Amount);
            if (settledCredit.IsFailure)
            {
                return settledCredit;
            }
        }

        return Result.Success();
    }

    private static Result CheckLines(
        IReadOnlyList<SettlementLine> lines,
        CustomerRef customerId,
        Currency currency)
    {
        // The same document twice is not a bigger amount; it is a caller who has lost track, and
        // adding the two together would be this module deciding what they meant.
        if (lines.Select(line => line.Item.Id).Distinct().Count() != lines.Count)
        {
            return FinanceErrors.Settlement.DuplicateItem;
        }

        foreach (SettlementLine line in lines)
        {
            if (line.Item.CustomerId != customerId)
            {
                return FinanceErrors.Settlement.DifferentCustomer(line.Item.DocumentNumber);
            }

            if (line.Item.Currency != currency)
            {
                return FinanceErrors.Settlement.CurrencyMismatch;
            }

            if (!line.Amount.IsPositive)
            {
                return FinanceErrors.Settlement.AmountNotPositive;
            }

            if (!line.Item.IsOutstanding)
            {
                return FinanceErrors.Settlement.NothingOutstanding(line.Item.DocumentNumber);
            }

            if (line.Amount > line.Item.Outstanding)
            {
                return FinanceErrors.Settlement.ExceedsOutstanding(
                    line.Item.DocumentNumber, line.Item.Outstanding.Amount);
            }
        }

        return Result.Success();
    }

    private static Money Sum(IReadOnlyList<SettlementLine> lines, Currency currency)
    {
        Money total = Money.Zero(currency);

        foreach (SettlementLine line in lines)
        {
            total += line.Amount;
        }

        return total;
    }
}
