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
