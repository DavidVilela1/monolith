using AutoPartsErp.Modules.Invoicing.Domain.Invoices.Events;
using AutoPartsErp.Modules.Invoicing.Domain.Series;
using AutoPartsErp.Modules.Invoicing.Domain.Signing;
using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Invoicing.Domain.Invoices;

/// <summary>
/// A document with legal weight: a number in a registered series, an ATCUD, a signature chained
/// to the document before it, and a QR code carrying all of it.
/// <para>
/// The unusual thing about this aggregate is that it is built in two moves. Lines are added while
/// it is a draft and nothing about it is fixed; then <see cref="Issue"/> takes a number from a
/// series, computes the totals, signs the result and freezes everything. After that the only
/// permitted change is voiding it, and even that leaves every figure exactly where it was.
/// </para>
/// <para>
/// That shape is forced by the law rather than chosen. A number handed out and not used is a gap
/// in a gapless sequence, so the number cannot be taken until the document is otherwise complete;
/// and the signature covers the total, so the total cannot move afterwards.
/// </para>
/// </summary>
public sealed class Invoice : AggregateRoot<InvoiceId>, IAuditable, ITenantScoped
{
    /// <summary>Longest permitted customer name snapshot.</summary>
    public const int MaxCustomerNameLength = 200;

    /// <summary>Longest permitted void reason.</summary>
    public const int MaxVoidReasonLength = 300;

    private readonly List<InvoiceLine> _lines = [];

    private Invoice(
        InvoiceId id,
        DocumentType type,
        CustomerRef customerId,
        string customerName,
        string? customerTaxNumber,
        string customerCountry,
        Currency currency,
        TaxRegion taxRegion,
        DateOnly documentDate,
        SalesOrderRef? salesOrderId)
        : base(id)
    {
        Type = type;
        CustomerId = customerId;
        CustomerName = customerName;
        CustomerTaxNumber = customerTaxNumber;
        CustomerCountry = customerCountry;
        CurrencyCode = currency.Code;
        TaxRegion = taxRegion;
        DocumentDate = documentDate;
        SalesOrderId = salesOrderId;
        Status = InvoiceStatus.Normal;
        CreatedBy = string.Empty;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private Invoice()
    {
    }
#pragma warning restore CS8618

    /// <summary>What kind of document this is.</summary>
    public DocumentType Type { get; private set; }

    /// <summary>The customer it is addressed to.</summary>
    public CustomerRef CustomerId { get; private set; }

    /// <summary>Their name, as it was on the day.</summary>
    public string CustomerName { get; private set; } = string.Empty;

    /// <summary>
    /// Their tax number, as it was on the day. Null for an unidentified customer, which is legal
    /// on a simplified invoice below the threshold and is most of a trade counter's morning.
    /// </summary>
    public string? CustomerTaxNumber { get; private set; }

    /// <summary>Their country, ISO two-letter.</summary>
    public string CustomerCountry { get; private set; } = "PT";

    /// <summary>The currency of every figure on the document.</summary>
    public string CurrencyCode { get; private set; } = Currency.Default.Code;

    /// <summary>
    /// Which set of Portuguese rates applies.
    /// <para>
    /// One region per document. A company with establishments on the mainland and in Madeira
    /// issues from one or the other, not both on one page — the QR code's J and K blocks exist
    /// for the case where it does, and nothing here produces them.
    /// </para>
    /// </summary>
    public TaxRegion TaxRegion { get; private set; }

    /// <summary>The date on the document.</summary>
    public DateOnly DocumentDate { get; private set; }

    /// <summary>The order it was raised against, when there was one.</summary>
    public SalesOrderRef? SalesOrderId { get; private set; }

    /// <summary>The series it was issued in. Null while it is still a draft.</summary>
    public DocumentSeriesId? SeriesId { get; private set; }

    /// <summary>Its number, e.g. <c>FT SERIE2026/35</c>. Empty while it is still a draft.</summary>
    public string DocumentNumber { get; private set; } = string.Empty;

    /// <summary>
    /// Its position in the series, from 1. Zero while it is still a draft.
    /// <para>
    /// Redundant with <see cref="DocumentNumber"/>, and stored anyway, because it is the only
    /// thing that answers "which document came last in this series" correctly. Sorted as text,
    /// <c>FT SERIE2026/9</c> comes after <c>FT SERIE2026/10</c> — and the document that comes last
    /// is the one the next signature chains onto, so getting that ordering wrong would break the
    /// chain at every tenth document and nowhere else.
    /// </para>
    /// </summary>
    public int SeriesNumber { get; private set; }

    /// <summary>Its unique code. Null while it is still a draft.</summary>
    public Atcud? Atcud { get; private set; }

    /// <summary>Its signature, and the four characters of it that get printed.</summary>
    public DocumentSignature? Signature { get; private set; }

    /// <summary>The QR payload, computed once at issue and stored as printed.</summary>
    public string? QrCode { get; private set; }

    /// <summary>
    /// When the record was created in the system.
    /// <para>
    /// Part of the signed string, and deliberately separate from the document date. Backdating a
    /// document is legal; backdating its entry into the system is not, and having both is what
    /// makes the difference visible.
    /// </para>
    /// </summary>
    public DateTimeOffset? SystemEntryDateUtc { get; private set; }

    /// <summary>Where the document stands.</summary>
    public InvoiceStatus Status { get; private set; }

    /// <summary>Why it was voided, when it was.</summary>
    public string? VoidReason { get; private set; }

    /// <summary>When it was voided.</summary>
    public DateTimeOffset? VoidedAtUtc { get; private set; }

    /// <summary>Its lines, in the order they appear on the page.</summary>
    public IReadOnlyList<InvoiceLine> Lines => [.. _lines.OrderBy(line => line.Number)];

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

    /// <summary>The currency of every figure on the document.</summary>
    public Currency Currency => Currency.FromCode(CurrencyCode);

    /// <summary>True until the document takes a number. Only then can anything be changed.</summary>
    public bool IsDraft => Signature is null;

    /// <summary>True once it has a number, a code and a signature.</summary>
    public bool IsIssued => !IsDraft;

    /// <summary>The totals, split by VAT category.</summary>
    public TaxSummary Taxes
    {
        get
        {
            var summary = default(TaxSummary);

            foreach (InvoiceLine line in _lines)
            {
                summary = summary.Add(line.VatRate, line.NetAmount, line.VatAmount);
            }

            return summary;
        }
    }

    /// <summary>What the customer is asked to pay.</summary>
    public Money GrossTotal => Money.Of(Taxes.GrossTotal, Currency);

    /// <summary>Starts a document, as a draft with no number.</summary>
    /// <param name="type">What kind of document it is.</param>
    /// <param name="customerId">Who it is for.</param>
    /// <param name="customerName">Their name.</param>
    /// <param name="customerTaxNumber">Their NIF, or null when they are not identified.</param>
    /// <param name="customerCountry">Their country, ISO two-letter.</param>
    /// <param name="currency">The currency of every figure.</param>
    /// <param name="taxRegion">Which set of rates applies.</param>
    /// <param name="documentDate">The date on the document.</param>
    /// <param name="salesOrderId">The order it is raised against, when there is one.</param>
    public static Result<Invoice> Draft(
        DocumentType type,
        CustomerRef customerId,
        string? customerName,
        string? customerTaxNumber,
        string? customerCountry,
        Currency currency,
        TaxRegion taxRegion,
        DateOnly documentDate,
        SalesOrderRef? salesOrderId = null)
    {
        ArgumentNullException.ThrowIfNull(currency);

        if (type == DocumentType.Unknown)
        {
            return InvoicingErrors.Document.TypeRequired;
        }

        if (customerId.IsEmpty)
        {
            return InvoicingErrors.Document.CustomerRequired;
        }

        if (string.IsNullOrWhiteSpace(customerName))
        {
            return InvoicingErrors.Document.CustomerNameRequired;
        }

        if (string.IsNullOrWhiteSpace(customerCountry) || customerCountry.Trim().Length != 2)
        {
            return InvoicingErrors.Document.CustomerCountryInvalid;
        }

        if (taxRegion == TaxRegion.Unknown)
        {
            return InvoicingErrors.Document.TaxRegionRequired;
        }

        // An invoice above the simplified-invoice threshold has to identify its customer. That
        // threshold is a configuration question rather than a domain one, so what is enforced
        // here is the part that never changes: an FT names somebody, an FS need not.
        if (type == DocumentType.Invoice && string.IsNullOrWhiteSpace(customerTaxNumber))
        {
            return InvoicingErrors.Document.InvoiceNeedsCustomerTaxNumber;
        }

        string name = customerName.Trim();

        return name.Length > MaxCustomerNameLength
            ? InvoicingErrors.Document.CustomerNameTooLong
            : new Invoice(
                InvoiceId.New(),
                type,
                customerId,
                name,
                string.IsNullOrWhiteSpace(customerTaxNumber) ? null : customerTaxNumber.Trim().ToUpperInvariant(),
                customerCountry.Trim().ToUpperInvariant(),
                currency,
                taxRegion,
                documentDate,
                salesOrderId);
    }

    /// <summary>Adds a line to a draft.</summary>
    /// <param name="partId">The part sold.</param>
    /// <param name="sku">Its SKU.</param>
    /// <param name="description">Its description.</param>
    /// <param name="quantity">How much was sold.</param>
    /// <param name="unitPrice">The price per unit, before discount.</param>
    /// <param name="discountPercent">The discount given, 0 to 100.</param>
    /// <param name="vatRate">The VAT rate applied.</param>
    public Result<InvoiceLineId> AddLine(
        PartRef partId,
        string? sku,
        string? description,
        Quantity quantity,
        Money unitPrice,
        decimal discountPercent,
        VatRate vatRate)
    {
        ArgumentNullException.ThrowIfNull(quantity);
        ArgumentNullException.ThrowIfNull(unitPrice);
        ArgumentNullException.ThrowIfNull(vatRate);

        if (IsIssued)
        {
            return InvoicingErrors.Document.AlreadyIssued;
        }

        if (unitPrice.Currency != Currency)
        {
            return InvoicingErrors.Line.CurrencyMismatch;
        }

        Result<InvoiceLine> line = InvoiceLine.Create(
            _lines.Count + 1, partId, sku, description, quantity, unitPrice, discountPercent, vatRate);

        if (line.IsFailure)
        {
            return Result.Failure<InvoiceLineId>(line.Error);
        }

        _lines.Add(line.Value);

        return line.Value.Id;
    }

    /// <summary>
    /// Takes a number from the series, signs the document and freezes it.
    /// <para>
    /// The series is passed in and mutated here, because taking a number and writing the document
    /// have to be one transaction: a number taken by a document that then fails to save is a gap,
    /// and a gap is what the whole mechanism exists to prevent.
    /// </para>
    /// </summary>
    /// <param name="series">The series to take a number from. Its state moves on.</param>
    /// <param name="signer">Signs the source string with the company's registered key.</param>
    /// <param name="previousSignature">
    /// The base64 signature of the document issued before this one in the same series, or null
    /// when this is the first. Fetched by the caller, because an aggregate cannot query.
    /// </param>
    /// <param name="issuerTaxNumber">The company's own NIF, for the QR code.</param>
    /// <param name="systemEntryDateUtc">When the record is being created.</param>
    public Result Issue(
        DocumentSeries series,
        IDocumentSigner signer,
        string? previousSignature,
        string issuerTaxNumber,
        DateTimeOffset systemEntryDateUtc)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(signer);
        ArgumentException.ThrowIfNullOrWhiteSpace(issuerTaxNumber);

        if (IsIssued)
        {
            return InvoicingErrors.Document.AlreadyIssued;
        }

        if (_lines.Count == 0)
        {
            return InvoicingErrors.Document.NoLines;
        }

        if (series.Type != Type)
        {
            return InvoicingErrors.Document.SeriesTypeMismatch;
        }

        if (series.Year != DocumentDate.Year)
        {
            return InvoicingErrors.Document.SeriesYearMismatch;
        }

        Result<DocumentNumber> number = series.TakeNextNumber();

        if (number.IsFailure)
        {
            return number.Error;
        }

        Result<Atcud> atcud = Atcud.Create(series.ValidationCode, number.Value.Number);

        if (atcud.IsFailure)
        {
            return atcud.Error;
        }

        string source = SignatureSource.Build(
            DocumentDate,
            systemEntryDateUtc,
            number.Value.Formatted,
            Taxes.GrossTotal,
            previousSignature);

        Result<DocumentSignature> signature = DocumentSignature.Create(signer.Sign(source));

        if (signature.IsFailure)
        {
            return signature.Error;
        }

        SeriesId = series.Id;
        DocumentNumber = number.Value.Formatted;
        SeriesNumber = number.Value.Number;
        Atcud = atcud.Value;
        Signature = signature.Value;
        SystemEntryDateUtc = systemEntryDateUtc;

        QrCode = QrCodePayload.Build(
            issuerTaxNumber,
            CustomerTaxNumber,
            CustomerCountry,
            Type,
            Status,
            DocumentDate,
            DocumentNumber,
            atcud.Value,
            TaxRegion,
            Taxes,
            signature.Value,
            signer.CertificateNumber);

        Raise(new InvoiceIssuedDomainEvent(
            Id,
            Type,
            DocumentNumber,
            atcud.Value.Value,
            CustomerId,
            Taxes.NetTotal,
            Taxes.VatTotal,
            Taxes.GrossTotal,
            CurrencyCode,
            DocumentDate));

        return Result.Success();
    }

    /// <summary>
    /// Voids the document.
    /// <para>
    /// It keeps its number, its figures and its place in the chain, and gains a status and a
    /// reason. Nothing is deleted, because the number was reported to the AT the moment it was
    /// issued and a missing number is worse than a cancelled one.
    /// </para>
    /// <para>
    /// Voiding is for a document raised in error and caught quickly. A document the customer has
    /// already acted on is corrected with a credit note instead — same reason, different remedy.
    /// </para>
    /// </summary>
    /// <param name="reason">Why it is being voided. Required, and it goes in the SAF-T export.</param>
    /// <param name="atUtc">When.</param>
    public Result Void(string? reason, DateTimeOffset atUtc)
    {
        if (IsDraft)
        {
            return InvoicingErrors.Document.NotIssued;
        }

        if (Status == InvoiceStatus.Voided)
        {
            return InvoicingErrors.Document.AlreadyVoided;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return InvoicingErrors.Document.VoidReasonRequired;
        }

        string trimmed = reason.Trim();

        if (trimmed.Length > MaxVoidReasonLength)
        {
            return InvoicingErrors.Document.VoidReasonTooLong;
        }

        // The QR code is left exactly as it was issued, still saying "N" in field E. It is a
        // record of what was printed and handed over, not a live view of the document — and the
        // status a tax inspector cares about comes from the SAF-T export, where it is a field in
        // its own right. Rewriting it here would produce a payload that never matched any piece
        // of paper.
        Status = InvoiceStatus.Voided;
        VoidReason = trimmed;
        VoidedAtUtc = atUtc;

        Raise(new InvoiceVoidedDomainEvent(Id, Type, DocumentNumber, CustomerId, Taxes.GrossTotal, trimmed));

        return Result.Success();
    }
}
