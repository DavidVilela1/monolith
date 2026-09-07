using AutoPartsErp.Modules.Finance.Domain;
using AutoPartsErp.Modules.Finance.Domain.Receipts;
using AutoPartsErp.Modules.Finance.Domain.Receivables;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Finance.Tests;

/// <summary>
/// Things to test with.
/// <para>
/// Every document gets a fresh identifier and a distinct number, so tests can run in any order
/// and a failure names something findable rather than "FT 2026/1" for the ninth time.
/// </para>
/// </summary>
internal static class Sample
{
    /// <summary>A customer, when the test does not care which.</summary>
    public static CustomerRef Customer { get; } = new(Guid.NewGuid());

    /// <summary>The date documents in these tests are raised on.</summary>
    public static DateOnly Raised { get; } = new(2026, 9, 7);

    /// <summary>Thirty days after that.</summary>
    public static DateOnly Due { get; } = new(2026, 10, 7);

    /// <summary>An invoice the customer owes.</summary>
    public static OpenItem Invoice(
        decimal amount,
        CustomerRef? customer = null,
        DateOnly? due = null,
        Currency? currency = null) =>
        Item(OpenItemKind.Invoice, amount, customer, due, currency);

    /// <summary>A credit note owed back to the customer.</summary>
    public static OpenItem CreditNote(
        decimal amount,
        CustomerRef? customer = null,
        Currency? currency = null) =>
        Item(OpenItemKind.CreditNote, amount, customer, null, currency);

    /// <summary>An item of any kind.</summary>
    public static OpenItem Item(
        OpenItemKind kind,
        decimal amount,
        CustomerRef? customer = null,
        DateOnly? due = null,
        Currency? currency = null) =>
        OpenItem.Raise(
            customer ?? Customer,
            new DocumentRef(Guid.NewGuid()),
            Prefix(kind) + " 2026/" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(),
            kind,
            Money.Of(amount, currency ?? Currency.Eur),
            Raised,
            due ?? Due).Value;

    /// <summary>Money received from a customer.</summary>
    public static Receipt Receipt(
        decimal amount,
        CustomerRef? customer = null,
        Currency? currency = null) =>
        Domain.Receipts.Receipt.Record(
            "RC-2026-" + Guid.NewGuid().ToString("N")[..5].ToUpperInvariant(),
            customer ?? Customer,
            Money.Of(amount, currency ?? Currency.Eur),
            new DateOnly(2026, 9, 20),
            ReceiptMethod.BankTransfer,
            "NTRF20260920",
            null).Value;

    /// <summary>Euros.</summary>
    public static Money Eur(decimal amount) => Money.Of(amount, Currency.Eur);

    private static string Prefix(OpenItemKind kind) => kind switch
    {
        OpenItemKind.CreditNote => "NC",
        OpenItemKind.DebitNote => "ND",
        _ => "FT",
    };
}
