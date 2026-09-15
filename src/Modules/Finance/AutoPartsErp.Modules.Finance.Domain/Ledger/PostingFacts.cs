namespace AutoPartsErp.Modules.Finance.Domain.Ledger;

/// <summary>
/// Every financial fact this system can post, and the amounts each one carries.
/// <para>
/// The catalogue exists so that a mapping can be checked when somebody writes it rather than
/// months later when it first fires. A rule naming <c>vat</c> against a fact that supplies
/// <c>tax</c> is a typo, and without this it is a typo that produces a silently unposted document
/// on a Tuesday in February.
/// </para>
/// <para>
/// <b>These are keys, not account codes.</b> A fact says "a sale was invoiced and the VAT on it
/// was 152,35"; what that lands on is the accountant's decision and lives in a
/// <see cref="PostingRule"/>. Nothing here presumes a chart of accounts.
/// </para>
/// <para>
/// Written out rather than reflected over anything. Reflection would keep itself up to date and
/// would also publish every fact somebody left half-wired, which is the opposite of what a
/// catalogue is for.
/// </para>
/// </summary>
public static class PostingFacts
{
    /// <summary>A sales document was issued to a customer.</summary>
    public const string SalesInvoiceIssued = "sales.invoice_issued";

    /// <summary>A sales document was voided.</summary>
    public const string SalesInvoiceVoided = "sales.invoice_voided";

    /// <summary>Money arrived from a customer.</summary>
    public const string CustomerReceipt = "sales.receipt";

    /// <summary>Stock left the warehouse against a sale, at what it had cost.</summary>
    public const string CostOfSale = "inventory.cost_of_sale";

    /// <summary>Stock was found short or over against a count.</summary>
    public const string StockAdjustment = "inventory.adjustment";

    /// <summary>A transfer arrived short and the difference was written off.</summary>
    public const string StockShrinkage = "inventory.shrinkage";

    /// <summary>
    /// A supplier's price differed from what the receipt was booked at.
    /// <para>
    /// The amount can go either way, and the rule is written for the direction that raises the
    /// value of stock. A supplier who charged less than expected flips both sides; see
    /// <see cref="PostingRule.Apply"/>.
    /// </para>
    /// </summary>
    public const string PriceVariance = "inventory.price_variance";

    /// <summary>A supplier's invoice was reconciled and became a debt.</summary>
    public const string SupplierInvoiceSettled = "purchasing.supplier_invoice_settled";

    /// <summary>Money left for a supplier.</summary>
    public const string SupplierPayment = "purchasing.supplier_payment";

    /// <summary>A rebate the supplier credited against a period.</summary>
    public const string RappelCredited = "purchasing.rappel_credited";

    /// <summary>The net of a document, before tax.</summary>
    public const string Net = "net";

    /// <summary>The tax on a document.</summary>
    public const string Vat = "vat";

    /// <summary>The total of a document, tax included.</summary>
    public const string Gross = "gross";

    /// <summary>A value carried on the stock ledger.</summary>
    public const string Value = "value";

    /// <summary>A rebate that came off a document.</summary>
    public const string Rebate = "rebate";

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Catalogue =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [SalesInvoiceIssued] = [Net, Vat, Gross],
            [SalesInvoiceVoided] = [Net, Vat, Gross],
            [CustomerReceipt] = [Gross],
            [CostOfSale] = [Value],
            [StockAdjustment] = [Value],
            [StockShrinkage] = [Value],
            [PriceVariance] = [Value],
            [SupplierInvoiceSettled] = [Net, Vat, Gross, Rebate],
            [SupplierPayment] = [Gross],
            [RappelCredited] = [Net, Vat, Gross],
        };

    /// <summary>Every fact that can be mapped, for showing somebody the list.</summary>
    public static IReadOnlyCollection<string> All => (IReadOnlyCollection<string>)Catalogue.Keys;

    /// <summary>True when the string names a fact this system actually raises.</summary>
    /// <param name="factType">The fact type.</param>
    public static bool IsKnown(string? factType) =>
        factType is not null && Catalogue.ContainsKey(factType);

    /// <summary>
    /// The amounts a fact carries, which are the only keys a rule for it may name.
    /// <para>
    /// Empty for a fact type nobody recognizes, so a caller that forgot to check gets a rule it
    /// cannot add a line to rather than one that maps nothing.
    /// </para>
    /// </summary>
    /// <param name="factType">The fact type.</param>
    public static IReadOnlyList<string> AmountKeysFor(string? factType) =>
        factType is not null && Catalogue.TryGetValue(factType, out IReadOnlyList<string>? keys)
            ? keys
            : [];

    /// <summary>True when a fact carries an amount under that key.</summary>
    /// <param name="factType">The fact type.</param>
    /// <param name="amountKey">The amount key.</param>
    public static bool Carries(string? factType, string? amountKey) =>
        amountKey is not null && AmountKeysFor(factType).Contains(amountKey, StringComparer.Ordinal);
}
