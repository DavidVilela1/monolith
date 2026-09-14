using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Finance.Domain.Payments;

/// <summary>
/// One line of a remittance: which of the supplier's documents this payment paid, and how much.
/// <para>
/// The document number is copied rather than joined, for the reason its sibling on the receipts
/// side gives: a remittance advice has to read on its own, and the number is a fact about the
/// moment of the match. It does not change, and nor should this row when something upstream is
/// corrected.
/// </para>
/// </summary>
public sealed class SupplierPaymentAllocation : Entity<SupplierPaymentAllocationId>, ITenantScoped
{
    private SupplierPaymentAllocation(
        SupplierPaymentAllocationId id,
        PayableItemId payableItemId,
        string documentNumber,
        Money amount,
        DateOnly allocatedOn)
        : base(id)
    {
        PayableItemId = payableItemId;
        DocumentNumber = documentNumber;
        Amount = amount;
        AllocatedOn = allocatedOn;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private SupplierPaymentAllocation()
    {
    }
#pragma warning restore CS8618

    /// <summary>The item this went against.</summary>
    public PayableItemId PayableItemId { get; private set; }

    /// <summary>The supplier's document number, copied at the moment of the match.</summary>
    public string DocumentNumber { get; private set; } = string.Empty;

    /// <summary>How much of the payment went against it.</summary>
    public Money Amount { get; private set; } = null!;

    /// <summary>The date the match was made, which may be after the money left.</summary>
    public DateOnly AllocatedOn { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; set; }

    /// <summary>Creates an allocation. Called by <see cref="SupplierPayment"/>, never directly.</summary>
    /// <param name="payableItemId">The item being paid.</param>
    /// <param name="documentNumber">Its number.</param>
    /// <param name="amount">How much of the payment goes against it.</param>
    /// <param name="allocatedOn">The date the match was made.</param>
    internal static SupplierPaymentAllocation Create(
        PayableItemId payableItemId,
        string documentNumber,
        Money amount,
        DateOnly allocatedOn) =>
        new(SupplierPaymentAllocationId.New(), payableItemId, documentNumber, amount, allocatedOn);
}
