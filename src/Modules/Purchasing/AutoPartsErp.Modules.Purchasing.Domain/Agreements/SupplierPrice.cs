using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Purchasing.Domain.Agreements;

/// <summary>
/// What one supplier charges for one part, from a given day.
/// <para>
/// Its own aggregate root rather than a collection on <see cref="SupplierAgreement"/>, and for the
/// same reason Pricing's entries are not children of a price list: a supplier's catalogue is tens
/// of thousands of parts, and correcting one price should not mean loading all of them.
/// </para>
/// <para>
/// A price is never edited in place. The supplier announces a rise from the first of October, the
/// buyer records it, and both rows stay: the old one explains the invoices already received and
/// the new one prices everything after. Overwriting the figure would leave the company unable to
/// say why a delivery in August cost what it did, which is the question an auditor asks and the
/// buyer asks the day the supplier's numbers stop matching theirs.
/// </para>
/// <para>
/// This is what makes entry automatic. When the pallet is counted, the price is not a thing
/// anybody types: it is looked up here for the day the goods arrived.
/// </para>
/// </summary>
public sealed class SupplierPrice : AggregateRoot<SupplierPriceId>, IAuditable, ISoftDeletable, ITenantScoped
{
    private SupplierPrice(
        SupplierPriceId id,
        SupplierRef supplierId,
        PartRef partId,
        Money unitPrice,
        DateOnly effectiveFrom,
        string? supplierPartNumber)
        : base(id)
    {
        SupplierId = supplierId;
        PartId = partId;
        UnitPrice = unitPrice;
        EffectiveFrom = effectiveFrom;
        SupplierPartNumber = supplierPartNumber;
        CreatedBy = string.Empty;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private SupplierPrice()
    {
    }
#pragma warning restore CS8618

    /// <summary>The supplier charging it.</summary>
    public SupplierRef SupplierId { get; private set; }

    /// <summary>The part.</summary>
    public PartRef PartId { get; private set; }

    /// <summary>What one unit costs, before any rebate.</summary>
    public Money UnitPrice { get; private set; } = null!;

    /// <summary>The first day this price applies.</summary>
    public DateOnly EffectiveFrom { get; private set; }

    /// <summary>
    /// Their reference for the part, as it appears on their paperwork.
    /// <para>
    /// Kept because a supplier's invoice does not carry the company's SKU, and matching a line to
    /// a part by description is how a delivery of brake pads gets booked against brake discs.
    /// </para>
    /// </summary>
    public string? SupplierPartNumber { get; private set; }

    /// <summary>Why the price moved. The buyer will want it when the supplier disputes the figure.</summary>
    public string? Note { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <inheritdoc />
    public string CreatedBy { get; set; } = string.Empty;

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; set; }

    /// <inheritdoc />
    public string? ModifiedBy { get; set; }

    /// <inheritdoc />
    public bool IsDeleted { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? DeletedAtUtc { get; set; }

    /// <inheritdoc />
    public string? DeletedBy { get; set; }

    /// <summary>Records what a supplier charges for a part from a given day.</summary>
    /// <param name="supplierId">The supplier.</param>
    /// <param name="partId">The part.</param>
    /// <param name="unitPrice">What one unit costs, before any rebate.</param>
    /// <param name="effectiveFrom">The first day it applies.</param>
    /// <param name="supplierPartNumber">Their reference for the part, if it is known.</param>
    public static Result<SupplierPrice> Agree(
        SupplierRef supplierId,
        PartRef partId,
        Money unitPrice,
        DateOnly effectiveFrom,
        string? supplierPartNumber = null)
    {
        ArgumentNullException.ThrowIfNull(unitPrice);

        if (supplierId.IsEmpty)
        {
            return PurchasingErrors.Agreement.SupplierRequired;
        }

        if (partId.IsEmpty)
        {
            return PurchasingErrors.Line.PartRequired;
        }

        // Zero is refused rather than allowed as "free". A supplier line at nothing is almost
        // always a price nobody filled in, and a shelf costed at zero reports every sale off it as
        // pure margin — the exact figure a branch would act on and the exact one that is wrong.
        if (!unitPrice.IsPositive)
        {
            return PurchasingErrors.Agreement.PriceNotPositive;
        }

        return new SupplierPrice(
            SupplierPriceId.New(),
            supplierId,
            partId,
            unitPrice,
            effectiveFrom,
            string.IsNullOrWhiteSpace(supplierPartNumber) ? null : supplierPartNumber.Trim());
    }

    /// <summary>
    /// Corrects a price that was typed wrong, in place.
    /// <para>
    /// For a mistake, not for a change. A supplier raising their prices is
    /// <see cref="Agree"/> with a new day, so both figures survive and every past delivery still
    /// explains itself; this is for the afternoon somebody notices the 4,50 should have been
    /// 45,00 and nothing has been received against it yet.
    /// </para>
    /// </summary>
    /// <param name="unitPrice">The figure it should have said.</param>
    /// <param name="note">Why it changed.</param>
    public Result Correct(Money unitPrice, string? note = null)
    {
        ArgumentNullException.ThrowIfNull(unitPrice);

        if (unitPrice.Currency != UnitPrice.Currency)
        {
            return PurchasingErrors.Agreement.PriceCurrencyMismatch;
        }

        if (!unitPrice.IsPositive)
        {
            return PurchasingErrors.Agreement.PriceNotPositive;
        }

        UnitPrice = unitPrice;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        return Result.Success();
    }

    /// <summary>Records their reference for the part, so their invoice lines can be matched.</summary>
    /// <param name="supplierPartNumber">Their reference, or null to clear.</param>
    public void SetSupplierPartNumber(string? supplierPartNumber) =>
        SupplierPartNumber =
            string.IsNullOrWhiteSpace(supplierPartNumber) ? null : supplierPartNumber.Trim();

    /// <summary>True when this price applies on the given day.</summary>
    /// <param name="on">The day the goods arrived.</param>
    public bool AppliesOn(DateOnly on) => on >= EffectiveFrom;
}
