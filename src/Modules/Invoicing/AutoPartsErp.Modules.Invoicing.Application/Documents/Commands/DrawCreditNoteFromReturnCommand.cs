using AutoPartsErp.ModuleContracts.Sales;
using AutoPartsErp.Modules.Invoicing.Domain;
using AutoPartsErp.Modules.Invoicing.Domain.Invoices;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Invoicing.Application.Documents.Commands;

/// <summary>
/// Draws a credit note for goods a customer has sent back.
/// <para>
/// The bridge in the other direction. <c>DrawFromSalesOrderCommand</c> turns goods that went out
/// into a document that charges for them; this turns goods that came back into a document that
/// gives the money back, and it has one extra thing to work out: which lines of which document
/// actually charged for them.
/// </para>
/// <para>
/// <b>The document is named, not guessed.</b> An order billed across three invoices could have
/// its return credited against any of them, and picking one would be this system choosing which
/// legal document to reverse. A credit note names exactly one original — that is what the
/// tax authority reads — so the person raising it names it too.
/// </para>
/// </summary>
/// <param name="CustomerReturnId">The return the goods came back on.</param>
/// <param name="InvoiceId">The document that charged for them.</param>
/// <param name="Reason">
/// Why the credit is being raised. Required, and it goes on every line of the SAF-T export. The
/// return has a reason of its own and it is not reused: why the customer sent something back and
/// why the company is giving money back are related and not the same sentence.
/// </param>
/// <param name="DocumentDate">The date on the credit note. Today when not given.</param>
public sealed record DrawCreditNoteFromReturnCommand(
    Guid CustomerReturnId,
    Guid InvoiceId,
    string Reason,
    DateOnly? DocumentDate = null) : ICommand<Guid>;

/// <summary>Checks the shape of a <see cref="DrawCreditNoteFromReturnCommand"/>.</summary>
public sealed class DrawCreditNoteFromReturnCommandValidator
    : IValidator<DrawCreditNoteFromReturnCommand>
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(
        DrawCreditNoteFromReturnCommand instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();

        if (instance.CustomerReturnId == Guid.Empty)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.CustomerReturnId), "required", "Say which return this is for."));
        }

        if (instance.InvoiceId == Guid.Empty)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.InvoiceId), "required",
                "Say which document charged for these goods."));
        }

        if (string.IsNullOrWhiteSpace(instance.Reason))
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Reason), "required", "Say why the credit is being raised."));
        }

        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

/// <summary>Draws the credit note.</summary>
public sealed class DrawCreditNoteFromReturnCommandHandler
    : ICommandHandler<DrawCreditNoteFromReturnCommand, Guid>
{
    private readonly IInvoiceRepository _invoices;
    private readonly ISalesOrderDirectory _sales;
    private readonly IInvoicingUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public DrawCreditNoteFromReturnCommandHandler(
        IInvoiceRepository invoices,
        ISalesOrderDirectory sales,
        IInvoicingUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _invoices = invoices;
        _sales = sales;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> HandleAsync(
        DrawCreditNoteFromReturnCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        CreditableReturn? returned = await _sales
            .GetCreditableReturnAsync(request.CustomerReturnId, cancellationToken)
            .ConfigureAwait(false);

        if (returned is null)
        {
            return Result.Failure<Guid>(
                InvoicingErrors.Credit.ReturnNotFound(request.CustomerReturnId.ToString()));
        }

        // Sales' judgement, not a status test done here. Whether goods are back is a rule about a
        // return, and reimplementing it in this module is how the two start disagreeing.
        if (!returned.CanCredit)
        {
            return Result.Failure<Guid>(
                InvoicingErrors.Credit.ReturnNotCreditable(returned.ReturnNumber, returned.Status));
        }

        var returnRef = new CustomerReturnRef(returned.CustomerReturnId);

        if (await _invoices.HasCreditNoteForReturnAsync(returnRef, cancellationToken)
            .ConfigureAwait(false))
        {
            return Result.Failure<Guid>(
                InvoicingErrors.Credit.ReturnAlreadyCredited(returned.ReturnNumber));
        }

        Invoice? original = await _invoices
            .GetByIdAsync(new InvoiceId(request.InvoiceId), cancellationToken)
            .ConfigureAwait(false);

        if (original is null)
        {
            return Result.Failure<Guid>(
                InvoicingErrors.Document.NotFound(request.InvoiceId.ToString()));
        }

        // The document has to be one raised for the order the goods went out on. Without this a
        // return could be credited against any invoice in the company, including another
        // customer's.
        if (original.SalesOrderId?.Value != returned.SalesOrderId)
        {
            return Result.Failure<Guid>(InvoicingErrors.Credit.ReturnNotOnDocument(
                returned.ReturnNumber, original.DocumentNumber ?? original.Id.ToString()));
        }

        Result<Dictionary<InvoiceLineId, Quantity>> quantities = MatchLines(original, returned);

        if (quantities.IsFailure)
        {
            return Result.Failure<Guid>(quantities.Error);
        }

        Result<Invoice> note = Invoice.DraftCreditNote(
            original,
            quantities.Value,
            request.Reason,
            request.DocumentDate ?? _clock.TodayUtc,
            returnRef);

        if (note.IsFailure)
        {
            return Result.Failure<Guid>(note.Error);
        }

        _invoices.Add(note.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return note.Value.Id.Value;
    }

    /// <summary>
    /// Finds the document line that charged for each returned line.
    /// <para>
    /// Matched on the sales order line, which both documents carry: the invoice line copied it
    /// when the document was drawn, and the return line has it because the goods came off that
    /// line of that order. Neither module knows the other's keys, and neither has to.
    /// </para>
    /// <para>
    /// A returned line with nothing to match is refused rather than skipped. It means the goods
    /// were billed on a different document or have not been billed at all, and quietly leaving
    /// them off would produce a credit note that looks complete and gives back less than the
    /// customer sent back — the kind of error found by the customer, months later.
    /// </para>
    /// </summary>
    private static Result<Dictionary<InvoiceLineId, Quantity>> MatchLines(
        Invoice original,
        CreditableReturn returned)
    {
        var quantities = new Dictionary<InvoiceLineId, Quantity>();

        foreach (CreditableReturnLine line in returned.Lines)
        {
            InvoiceLine? billed = original.Lines.FirstOrDefault(
                candidate => candidate.SalesOrderLineId?.Value == line.SalesOrderLineId);

            if (billed is null)
            {
                return Result.Failure<Dictionary<InvoiceLineId, Quantity>>(
                    InvoicingErrors.Credit.ReturnedLineNotBilled(
                        line.Sku, original.DocumentNumber ?? original.Id.ToString()));
            }

            // FromCode rather than TryFromCode, matching the order bridge beside it. The unit
            // came from Sales, not from a caller, and a code this system does not know would be a
            // defect in the contract rather than bad input to validate against.
            Result<Quantity> quantity = Quantity.Create(
                line.Quantity, UnitOfMeasure.FromCode(line.UnitCode));

            if (quantity.IsFailure)
            {
                return Result.Failure<Dictionary<InvoiceLineId, Quantity>>(quantity.Error);
            }

            // The aggregate refuses more than the line has left to credit, so a second return
            // against a line already credited in full is stopped there rather than here.
            quantities[billed.Id] = quantity.Value;
        }

        return quantities;
    }
}
