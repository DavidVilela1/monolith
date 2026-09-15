using AutoPartsErp.SharedKernel.Primitives;

namespace AutoPartsErp.Modules.Finance.Domain.Ledger.Events;

/// <summary>
/// An entry reached the ledger and the balances moved.
/// <para>
/// Carries the total of one side rather than every line. Anything that needs the lines can load
/// the entry; what a listener usually wants to know is that a period has changed, and by how much.
/// </para>
/// </summary>
/// <param name="JournalEntryId">The entry.</param>
/// <param name="Number">Our number for it.</param>
/// <param name="EntryDate">The day it belongs to, which decides its period.</param>
/// <param name="Source">Where it came from.</param>
/// <param name="Description">What it is for.</param>
/// <param name="Total">The total of each side, which are equal.</param>
/// <param name="CurrencyCode">The currency.</param>
public sealed record JournalEntryPostedDomainEvent(
    JournalEntryId JournalEntryId,
    string Number,
    DateOnly EntryDate,
    JournalSource Source,
    string Description,
    decimal Total,
    string CurrencyCode) : DomainEvent;

/// <summary>
/// A month was closed and nothing more is posted into it.
/// </summary>
/// <param name="AccountingPeriodId">The period.</param>
/// <param name="Year">The calendar year.</param>
/// <param name="Month">The calendar month.</param>
/// <param name="From">Its first day.</param>
/// <param name="To">Its last day.</param>
public sealed record AccountingPeriodClosedDomainEvent(
    AccountingPeriodId AccountingPeriodId,
    int Year,
    int Month,
    DateOnly From,
    DateOnly To) : DomainEvent;

/// <summary>
/// A closed month was opened again.
/// <para>
/// Worth its own event rather than being folded into the close. Closing is routine; reopening is
/// somebody deciding that a reported figure was wrong, and it is the one an auditor looks for.
/// </para>
/// </summary>
/// <param name="AccountingPeriodId">The period.</param>
/// <param name="Year">The calendar year.</param>
/// <param name="Month">The calendar month.</param>
/// <param name="Reason">Why.</param>
public sealed record AccountingPeriodReopenedDomainEvent(
    AccountingPeriodId AccountingPeriodId,
    int Year,
    int Month,
    string Reason) : DomainEvent;
