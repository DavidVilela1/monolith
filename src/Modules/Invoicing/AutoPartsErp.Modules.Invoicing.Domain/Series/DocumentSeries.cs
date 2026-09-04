using AutoPartsErp.Modules.Invoicing.Domain.Series.Events;
using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Invoicing.Domain.Series;

/// <summary>
/// A registered run of document numbers: one document type, one year, one unbroken sequence.
/// <para>
/// This is the aggregate Portuguese law actually cares about. A series has to be declared to the
/// AT before anything is issued in it, and what comes back is a validation code that becomes half
/// of every ATCUD the series produces. Without that code the series cannot legally issue, which
/// is why <see cref="Activate"/> refuses without one rather than letting somebody find out at the
/// first audit.
/// </para>
/// <para>
/// The numbers must be sequential and gapless. This aggregate is the only thing that hands one
/// out, and it hands them out one at a time — no reservation, no batching, no "take ten and use
/// them as needed". A number taken and not used is a gap, and a gap is the thing the whole
/// mechanism exists to make impossible.
/// </para>
/// </summary>
public sealed class DocumentSeries : AggregateRoot<DocumentSeriesId>, IAuditable, ITenantScoped
{
    /// <summary>Longest permitted series code.</summary>
    public const int MaxCodeLength = 20;

    /// <summary>Longest permitted AT validation code.</summary>
    public const int MaxValidationCodeLength = 20;

    private DocumentSeries(
        DocumentSeriesId id,
        DocumentType type,
        string code,
        int year)
        : base(id)
    {
        Type = type;
        Code = code;
        Year = year;
        NextNumber = 1;
        Status = SeriesStatus.Registered;
        CreatedBy = string.Empty;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private DocumentSeries()
    {
    }
#pragma warning restore CS8618

    /// <summary>What kind of document this series numbers.</summary>
    public DocumentType Type { get; private set; }

    /// <summary>
    /// The series identifier, as it appears in the document number and as it was declared to the
    /// AT. In <c>FT SERIE2026/35</c> this is <c>SERIE2026</c>.
    /// </summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>
    /// The year the series belongs to.
    /// <para>
    /// Not a rule of the law — a series may legally span years — but a convention worth enforcing
    /// here, because a series that restarts every January is the only one an accountant can
    /// reconcile against a year's VAT returns without counting backwards.
    /// </para>
    /// </summary>
    public int Year { get; private set; }

    /// <summary>
    /// The code the AT returns when the series is declared. Half of every ATCUD this series
    /// produces, and the reason a series cannot issue before it is registered.
    /// </summary>
    public string? ValidationCode { get; private set; }

    /// <summary>The next number this series will hand out.</summary>
    public int NextNumber { get; private set; }

    /// <summary>Where the series is in its life.</summary>
    public SeriesStatus Status { get; private set; }

    /// <summary>When the AT validation code was recorded.</summary>
    public DateTimeOffset? ValidatedAtUtc { get; private set; }

    /// <summary>When the series was closed to new documents.</summary>
    public DateTimeOffset? ClosedAtUtc { get; private set; }

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

    /// <summary>True while the series may hand out numbers.</summary>
    public bool CanIssue => Status == SeriesStatus.Active && !string.IsNullOrEmpty(ValidationCode);

    /// <summary>How many documents the series has issued.</summary>
    public int IssuedCount => NextNumber - 1;

    /// <summary>Opens a series, before it has been declared to the AT.</summary>
    /// <param name="type">What kind of document it numbers.</param>
    /// <param name="code">The series identifier, e.g. <c>SERIE2026</c>.</param>
    /// <param name="year">The year it belongs to.</param>
    public static Result<DocumentSeries> Open(DocumentType type, string? code, int year)
    {
        if (type == DocumentType.Unknown)
        {
            return InvoicingErrors.Series.TypeRequired;
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return InvoicingErrors.Series.CodeRequired;
        }

        string normalized = code.Trim().ToUpperInvariant();

        if (normalized.Length > MaxCodeLength)
        {
            return InvoicingErrors.Series.CodeTooLong;
        }

        // The code goes into the document number, which goes into the QR code and the SAF-T
        // export. A space or a slash there would break the field that separates the series from
        // the number, and the AT rejects the file rather than guessing where one ends.
        foreach (char character in normalized)
        {
            if (!char.IsAsciiLetterOrDigit(character))
            {
                return InvoicingErrors.Series.CodeInvalidCharacters;
            }
        }

        if (year is < 2000 or > 2999)
        {
            return InvoicingErrors.Series.YearImplausible;
        }

        var series = new DocumentSeries(DocumentSeriesId.New(), type, normalized, year);
        series.Raise(new DocumentSeriesOpenedDomainEvent(series.Id, type, normalized, year));

        return series;
    }

    /// <summary>
    /// Records the validation code the AT returned for this series.
    /// <para>
    /// Recorded once and never changed. The code is baked into every ATCUD the series has already
    /// produced, so altering it would silently invalidate every document issued so far.
    /// </para>
    /// </summary>
    /// <param name="validationCode">The code from the AT, e.g. <c>CSDF7T5H</c>.</param>
    /// <param name="atUtc">When it was recorded.</param>
    public Result Validate(string? validationCode, DateTimeOffset atUtc)
    {
        if (Status == SeriesStatus.Closed)
        {
            return InvoicingErrors.Series.Closed;
        }

        if (!string.IsNullOrEmpty(ValidationCode))
        {
            return InvoicingErrors.Series.AlreadyValidated;
        }

        if (string.IsNullOrWhiteSpace(validationCode))
        {
            return InvoicingErrors.Series.ValidationCodeRequired;
        }

        string normalized = validationCode.Trim().ToUpperInvariant();

        if (normalized.Length > MaxValidationCodeLength)
        {
            return InvoicingErrors.Series.ValidationCodeTooLong;
        }

        // A hyphen would be indistinguishable from the one that separates the code from the
        // number inside an ATCUD, and anything the AT has never issued is a typo worth catching
        // before it reaches a thousand documents.
        foreach (char character in normalized)
        {
            if (!char.IsAsciiLetterOrDigit(character))
            {
                return InvoicingErrors.Series.ValidationCodeInvalidCharacters;
            }
        }

        ValidationCode = normalized;
        ValidatedAtUtc = atUtc;

        return Result.Success();
    }

    /// <summary>Puts the series into service. It must have its validation code first.</summary>
    public Result Activate()
    {
        if (Status == SeriesStatus.Closed)
        {
            return InvoicingErrors.Series.Closed;
        }

        if (Status == SeriesStatus.Active)
        {
            return InvoicingErrors.Series.AlreadyActive;
        }

        if (string.IsNullOrEmpty(ValidationCode))
        {
            return InvoicingErrors.Series.NotValidated;
        }

        Status = SeriesStatus.Active;
        Raise(new DocumentSeriesActivatedDomainEvent(Id, Type, Code, ValidationCode));

        return Result.Success();
    }

    /// <summary>
    /// Closes the series to new documents.
    /// <para>
    /// One-way, and the documents already in it stay exactly as they are. Closing is what happens
    /// at a year end, or when a series is abandoned — and the AT has to be told, because an open
    /// series that stops producing documents looks like missing paperwork.
    /// </para>
    /// </summary>
    /// <param name="atUtc">When it was closed.</param>
    public Result Close(DateTimeOffset atUtc)
    {
        if (Status == SeriesStatus.Closed)
        {
            return InvoicingErrors.Series.Closed;
        }

        Status = SeriesStatus.Closed;
        ClosedAtUtc = atUtc;
        Raise(new DocumentSeriesClosedDomainEvent(Id, Type, Code, IssuedCount));

        return Result.Success();
    }

    /// <summary>
    /// Hands out the next number, and moves the series on.
    /// <para>
    /// The only way a number leaves this aggregate. It changes state, so a caller that takes a
    /// number and then fails must roll the whole transaction back — which is exactly why the
    /// document and this increment are written together, and why nothing here offers to "peek" at
    /// the next number without taking it.
    /// </para>
    /// </summary>
    public Result<DocumentNumber> TakeNextNumber()
    {
        if (Status == SeriesStatus.Closed)
        {
            return InvoicingErrors.Series.Closed;
        }

        if (Status != SeriesStatus.Active)
        {
            return InvoicingErrors.Series.NotActive;
        }

        if (string.IsNullOrEmpty(ValidationCode))
        {
            return InvoicingErrors.Series.NotValidated;
        }

        int number = NextNumber;
        NextNumber = number + 1;

        return new DocumentNumber(Type, Code, number);
    }
}

/// <summary>
/// A document's number, as it is printed and as the tax authority expects it.
/// <para>
/// <c>FT SERIE2026/35</c>: the type code, a space, the series, a slash, the number. That exact
/// shape goes in field G of the QR code and in <c>InvoiceNo</c> in a SAF-T export, and it is part
/// of the string that gets signed — so it is built in one place rather than formatted at each
/// point of use.
/// </para>
/// </summary>
/// <param name="Type">The document type.</param>
/// <param name="SeriesCode">The series it was issued in.</param>
/// <param name="Number">Its position in that series, from 1.</param>
public readonly record struct DocumentNumber(DocumentType Type, string SeriesCode, int Number)
{
    /// <summary>The number as printed, e.g. <c>FT SERIE2026/35</c>.</summary>
    public string Formatted => $"{Type.Code()} {SeriesCode}/{Number}";

    /// <inheritdoc />
    public override string ToString() => Formatted;
}
