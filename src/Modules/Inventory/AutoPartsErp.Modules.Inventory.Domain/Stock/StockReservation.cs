using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Inventory.Domain.Stock;

/// <summary>
/// A claim on stock that is physically present but already promised to someone.
/// <para>
/// Reservations are what stop the same brake disc being sold twice in the ten minutes between a
/// counter quote and the picker reaching the shelf. They reduce available quantity without
/// touching on-hand quantity, so the warehouse count still matches reality while the commercial
/// promise is honoured.
/// </para>
/// <para>
/// Expiry matters as much as creation. A quote that nobody converts must release its stock
/// automatically, or the shelf slowly fills with quantity that is reserved for orders that will
/// never happen — a failure mode that looks exactly like "we are out of stock".
/// </para>
/// </summary>
public sealed class StockReservation : Entity<ReservationId>
{
    private StockReservation(
        ReservationId id,
        Quantity quantity,
        MovementReference reference,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? expiresAtUtc)
        : base(id)
    {
        Quantity = quantity;
        Reference = reference;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        Status = ReservationStatus.Active;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private StockReservation()
    {
    }
#pragma warning restore CS8618

    /// <summary>How much is claimed.</summary>
    public Quantity Quantity { get; private set; } = null!;

    /// <summary>What claimed it: a quote, a sales order, a works order.</summary>
    public MovementReference Reference { get; private set; } = null!;

    /// <summary>When the claim was made.</summary>
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>When the claim lapses on its own, if it does.</summary>
    public DateTimeOffset? ExpiresAtUtc { get; private set; }

    /// <summary>Where the claim stands.</summary>
    public ReservationStatus Status { get; private set; }

    /// <summary>True while the claim still holds stock back.</summary>
    public bool IsActive => Status == ReservationStatus.Active;

    /// <summary>Creates an active reservation.</summary>
    internal static StockReservation Create(
        Quantity quantity,
        MovementReference reference,
        DateTimeOffset now,
        DateTimeOffset? expiresAtUtc) =>
        new(ReservationId.New(), quantity, reference, now, expiresAtUtc);

    /// <summary>True when the reservation has passed its expiry.</summary>
    public bool HasExpired(DateTimeOffset now) =>
        IsActive && ExpiresAtUtc is { } expiry && expiry <= now;

    internal void Release() => Status = ReservationStatus.Released;

    internal void Expire() => Status = ReservationStatus.Expired;

    internal void Fulfil() => Status = ReservationStatus.Fulfilled;
}

/// <summary>Where a reservation stands.</summary>
public enum ReservationStatus
{
    /// <summary>Unspecified. Never persisted.</summary>
    Unknown = 0,

    /// <summary>Still holding stock back.</summary>
    Active = 1,

    /// <summary>Cancelled before it was picked.</summary>
    Released = 2,

    /// <summary>Lapsed because nobody acted on it in time.</summary>
    Expired = 3,

    /// <summary>The stock was actually issued against it.</summary>
    Fulfilled = 4,
}
