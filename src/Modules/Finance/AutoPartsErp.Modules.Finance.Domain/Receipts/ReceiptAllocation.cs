using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Finance.Domain.Receipts;

/// <summary>
/// One document that one receipt paid, and how much of it.
/// <para>
/// This row is the answer to "what did this payment pay", and it is why the module allocates
/// rather than keeping a balance. Five hundred euros against a customer's balance tells nobody
/// anything six weeks later; five hundred euros split three hundred against FT 2026/12 and two
/// hundred against FT 2026/15 reconciles against the customer's own ledger line by line.
/// </para>
/// <para>
/// The document number is copied rather than joined. A statement is read far more often than it
/// is written, and the number is a fact about the moment of the match — it does not change, and
/// nor should this row when something upstream is corrected.
/// </para>
/// </summary>
public sealed class ReceiptAllocation : Entity<ReceiptAllocationId>, ITenantScoped
{
    private ReceiptAllocation(
        ReceiptAllocationId id,
        OpenItemId openItemId,
        string documentNumber,
        Money amount,
        DateOnly allocatedOn)
        : base(id)
    {
        OpenItemId = openItemId;
        DocumentNumber = documentNumber;
        Amount = amount;
        AllocatedOn = allocatedOn;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private ReceiptAllocation()
    {
    }
#pragma warning restore CS8618

    /// <summary>The item this went against.</summary>
    public OpenItemId OpenItemId { get; private set; }

    /// <summary>That item's document number, copied at the moment of the match.</summary>
    public string DocumentNumber { get; private set; } = string.Empty;

    /// <summary>How much of the receipt went against it.</summary>
    public Money Amount { get; private set; } = null!;

    /// <summary>The date the match was made, which may be after the money arrived.</summary>
    public DateOnly AllocatedOn { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; set; }

    /// <summary>Creates an allocation. Called by <see cref="Receipt"/>, never directly.</summary>
    /// <param name="openItemId">The item being paid.</param>
    /// <param name="documentNumber">Its number.</param>
    /// <param name="amount">How much goes against it.</param>
    /// <param name="allocatedOn">The date of the match.</param>
    internal static ReceiptAllocation Create(
        OpenItemId openItemId,
        string documentNumber,
        Money amount,
        DateOnly allocatedOn) =>

        // Copied, never adopted. The same Money instance was handed to the item being settled in
        // the same breath, and an owned entity belongs to one owner - see the rule in the README,
        // and the afternoon it cost in Invoicing.
        new(ReceiptAllocationId.New(), openItemId, documentNumber, amount.Copy(), allocatedOn);
}
