using AutoPartsErp.ModuleContracts.Catalog;
using AutoPartsErp.Modules.Invoicing.Application.Options;
using AutoPartsErp.Modules.Invoicing.Domain;
using AutoPartsErp.Modules.Invoicing.Domain.Invoices;
using AutoPartsErp.Modules.Invoicing.Domain.Series;
using AutoPartsErp.Modules.Invoicing.Domain.Signing;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Invoicing.Application.Documents.Commands;

/// <summary>Starts a document, as a draft with no number.</summary>
/// <param name="Type">FT, FS, FR, NC or ND.</param>
/// <param name="CustomerId">Who it is for.</param>
/// <param name="CustomerName">Their name, snapshotted onto the document.</param>
/// <param name="CustomerTaxNumber">Their NIF. Required for an FT, optional for an FS.</param>
/// <param name="CustomerCountry">Their country, ISO two-letter.</param>
/// <param name="CurrencyCode">The currency of every figure.</param>
/// <param name="DocumentDate">The date on the document. Defaults to today.</param>
/// <param name="SalesOrderId">The order it is raised against, when there is one.</param>
public sealed record CreateDocumentCommand(
    string Type,
    Guid CustomerId,
    string CustomerName,
    string? CustomerTaxNumber = null,
    string CustomerCountry = "PT",
    string CurrencyCode = "EUR",
    DateOnly? DocumentDate = null,
    Guid? SalesOrderId = null) : ICommand<Guid>;

/// <summary>Checks the shape of a <see cref="CreateDocumentCommand"/>.</summary>
public sealed class CreateDocumentCommandValidator : IValidator<CreateDocumentCommand>
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(
        CreateDocumentCommand instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();

        if (!DocumentTypeCodes.TryFromCode(instance.Type, out _))
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Type), "unknown_type",
                $"'{instance.Type}' is not a document type this module issues."));
        }

        if (instance.CustomerId == Guid.Empty)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.CustomerId), "required", "A customer is required."));
        }

        if (string.IsNullOrWhiteSpace(instance.CustomerName))
        {
            failures.Add(new ValidationFailure(
                nameof(instance.CustomerName), "required", "A customer name is required."));
        }

        if (!Currency.TryFromCode(instance.CurrencyCode, out _))
        {
            failures.Add(new ValidationFailure(
                nameof(instance.CurrencyCode), "unknown_currency",
                $"'{instance.CurrencyCode}' is not a supported currency."));
        }

        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

/// <summary>Creates the draft.</summary>
public sealed class CreateDocumentCommandHandler : ICommandHandler<CreateDocumentCommand, Guid>
{
    private readonly IInvoiceRepository _invoices;
    private readonly IInvoicingUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;
    private readonly InvoicingOptions _options;

    /// <summary>Initializes the handler.</summary>
    public CreateDocumentCommandHandler(
        IInvoiceRepository invoices,
        IInvoicingUnitOfWork unitOfWork,
        IDateTimeProvider clock,
        InvoicingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _invoices = invoices;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _options = options;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> HandleAsync(
        CreateDocumentCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        DocumentTypeCodes.TryFromCode(request.Type, out DocumentType type);

        Result<Invoice> invoice = Invoice.Draft(
            type,
            new CustomerRef(request.CustomerId),
            request.CustomerName,
            request.CustomerTaxNumber,
            request.CustomerCountry,
            Currency.FromCode(request.CurrencyCode),

            // The region is a property of the establishment doing the invoicing, not of the
            // document, so it comes from configuration rather than from the caller. A company
            // with establishments in two regions runs two deployments or two configurations.
            _options.TaxRegion,
            request.DocumentDate ?? _clock.TodayUtc,
            request.SalesOrderId is { } orderId ? new SalesOrderRef(orderId) : null);

        if (invoice.IsFailure)
        {
            return Result.Failure<Guid>(invoice.Error);
        }

        _invoices.Add(invoice.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return invoice.Value.Id.Value;
    }
}

/// <summary>Adds a line to a draft.</summary>
/// <param name="InvoiceId">The document.</param>
/// <param name="PartId">The part sold.</param>
/// <param name="Quantity">How much, in the part's stocking unit.</param>
/// <param name="UnitPrice">The price per unit, before discount.</param>
/// <param name="DiscountPercent">The discount given, 0 to 100.</param>
/// <param name="VatCategory">ISE, RED, INT or NOR.</param>
/// <param name="VatPercent">The rate. Ignored for an exempt line.</param>
/// <param name="ExemptionCode">The tax authority's code, required when exempt.</param>
/// <param name="ExemptionReason">The legal basis, required when exempt.</param>
public sealed record AddDocumentLineCommand(
    Guid InvoiceId,
    Guid PartId,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPercent = 0m,
    string VatCategory = "NOR",
    decimal VatPercent = 23m,
    string? ExemptionCode = null,
    string? ExemptionReason = null) : ICommand<Guid>;

/// <summary>Adds the line.</summary>
public sealed class AddDocumentLineCommandHandler : ICommandHandler<AddDocumentLineCommand, Guid>
{
    private readonly IInvoiceRepository _invoices;
    private readonly ICatalogDirectory _catalogue;
    private readonly IInvoicingUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public AddDocumentLineCommandHandler(
        IInvoiceRepository invoices,
        ICatalogDirectory catalogue,
        IInvoicingUnitOfWork unitOfWork)
    {
        _invoices = invoices;
        _catalogue = catalogue;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> HandleAsync(
        AddDocumentLineCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Invoice? invoice = await _invoices
            .GetByIdAsync(new InvoiceId(request.InvoiceId), cancellationToken)
            .ConfigureAwait(false);

        if (invoice is null)
        {
            return Result.Failure<Guid>(InvoicingErrors.Document.NotFound(request.InvoiceId.ToString()));
        }

        if (invoice.IsIssued)
        {
            return Result.Failure<Guid>(InvoicingErrors.Document.AlreadyIssued);
        }

        // The catalogue names the part, exactly as it does on a sales line. Sellability is not
        // checked: a credit note for a part withdrawn last month is a normal document, and
        // refusing it would leave somebody unable to correct their own paperwork.
        PartDescriptor? part = await _catalogue
            .GetAsync(request.PartId, cancellationToken)
            .ConfigureAwait(false);

        if (part is null)
        {
            return Result.Failure<Guid>(InvoicingErrors.Line.PartRequired);
        }

        Result<VatRate> rate = BuildRate(request);

        if (rate.IsFailure)
        {
            return Result.Failure<Guid>(rate.Error);
        }

        Result<Quantity> quantity = Quantity.Create(
            request.Quantity, UnitOfMeasure.FromCode(part.StockUnitCode));

        if (quantity.IsFailure)
        {
            return Result.Failure<Guid>(quantity.Error);
        }

        Result<InvoiceLineId> line = invoice.AddLine(
            new PartRef(part.PartId),
            part.Sku,
            part.Name,
            quantity.Value,
            Money.Of(request.UnitPrice, invoice.Currency),
            request.DiscountPercent,
            rate.Value);

        if (line.IsFailure)
        {
            return Result.Failure<Guid>(line.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return line.Value.Value;
    }

    private static Result<VatRate> BuildRate(AddDocumentLineCommand request)
    {
        if (string.Equals(request.VatCategory, "ISE", StringComparison.OrdinalIgnoreCase))
        {
            return VatRate.ExemptWith(request.ExemptionCode, request.ExemptionReason);
        }

        VatCategory category = request.VatCategory?.Trim().ToUpperInvariant() switch
        {
            "RED" => Domain.Invoices.VatCategory.Reduced,
            "INT" => Domain.Invoices.VatCategory.Intermediate,
            "NOR" => Domain.Invoices.VatCategory.Standard,
            _ => Domain.Invoices.VatCategory.Unknown,
        };

        return VatRate.Of(category, request.VatPercent);
    }
}

/// <summary>
/// Takes a number from a series, signs the document and freezes it.
/// <para>
/// The one operation in this system that runs inside an explicit transaction. It has to: the
/// series is locked while the number is taken, and the lock must still be held when the document
/// is written, or two tills could take the same number.
/// </para>
/// </summary>
/// <param name="InvoiceId">The document.</param>
/// <param name="SeriesId">
/// The series to issue in, or null to use the live one for this document type and year.
/// </param>
public sealed record IssueDocumentCommand(Guid InvoiceId, Guid? SeriesId = null) : ICommand;

/// <summary>Issues the document.</summary>
public sealed class IssueDocumentCommandHandler : ICommandHandler<IssueDocumentCommand>
{
    private readonly IInvoiceRepository _invoices;
    private readonly IDocumentSeriesRepository _series;
    private readonly IDocumentSigner _signer;
    private readonly IInvoicingUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;
    private readonly InvoicingOptions _options;

    /// <summary>Initializes the handler.</summary>
    public IssueDocumentCommandHandler(
        IInvoiceRepository invoices,
        IDocumentSeriesRepository series,
        IDocumentSigner signer,
        IInvoicingUnitOfWork unitOfWork,
        IDateTimeProvider clock,
        InvoicingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _invoices = invoices;
        _series = series;
        _signer = signer;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _options = options;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        IssueDocumentCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using IInvoicingTransaction transaction = await _unitOfWork
            .BeginAsync(cancellationToken)
            .ConfigureAwait(false);

        Invoice? invoice = await _invoices
            .GetByIdAsync(new InvoiceId(request.InvoiceId), cancellationToken)
            .ConfigureAwait(false);

        if (invoice is null)
        {
            return InvoicingErrors.Document.NotFound(request.InvoiceId.ToString());
        }

        if (invoice.IsIssued)
        {
            return InvoicingErrors.Document.AlreadyIssued;
        }

        DocumentSeries? series = request.SeriesId is { } seriesId
            ? await _series.GetForIssuingAsync(new DocumentSeriesId(seriesId), cancellationToken)
                .ConfigureAwait(false)
            : await FindActiveAsync(invoice, cancellationToken).ConfigureAwait(false);

        if (series is null)
        {
            return InvoicingErrors.Series.NotFound(
                request.SeriesId?.ToString() ?? $"{invoice.Type.Code()} {invoice.DocumentDate.Year}");
        }

        // The previous document's signature, read after the lock is held. Reading it before would
        // race: another till could issue between the read and the write, and this document would
        // chain onto a link that is no longer the last one.
        string? previousSignature = await _invoices
            .GetLastSignatureAsync(series.Id, cancellationToken)
            .ConfigureAwait(false);

        Result issued = invoice.Issue(
            series, _signer, previousSignature, _options.IssuerTaxNumber, _clock.UtcNow);

        if (issued.IsFailure)
        {
            return issued;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    private async Task<DocumentSeries?> FindActiveAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        DocumentSeries? active = await _series
            .GetActiveAsync(invoice.Type, invoice.DocumentDate.Year, cancellationToken)
            .ConfigureAwait(false);

        // Found without a lock, then reloaded with one. Two queries rather than one, and worth it:
        // the first is an index lookup that does not block anybody, and only the row it finds gets
        // locked.
        return active is null
            ? null
            : await _series.GetForIssuingAsync(active.Id, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Voids an issued document. It keeps its number, its figures and its signature.</summary>
/// <param name="InvoiceId">The document.</param>
/// <param name="Reason">Why. Required, and it goes on the SAF-T export.</param>
public sealed record VoidDocumentCommand(Guid InvoiceId, string Reason) : ICommand;

/// <summary>Voids the document.</summary>
public sealed class VoidDocumentCommandHandler : ICommandHandler<VoidDocumentCommand>
{
    private readonly IInvoiceRepository _invoices;
    private readonly IInvoicingUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public VoidDocumentCommandHandler(
        IInvoiceRepository invoices,
        IInvoicingUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _invoices = invoices;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        VoidDocumentCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Invoice? invoice = await _invoices
            .GetByIdAsync(new InvoiceId(request.InvoiceId), cancellationToken)
            .ConfigureAwait(false);

        if (invoice is null)
        {
            return InvoicingErrors.Document.NotFound(request.InvoiceId.ToString());
        }

        Result voided = invoice.Void(request.Reason, _clock.UtcNow);

        if (voided.IsFailure)
        {
            return voided;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
