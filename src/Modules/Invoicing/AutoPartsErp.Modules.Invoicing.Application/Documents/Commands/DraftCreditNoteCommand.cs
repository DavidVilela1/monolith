using AutoPartsErp.Modules.Invoicing.Domain;
using AutoPartsErp.Modules.Invoicing.Domain.Invoices;
using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Invoicing.Application.Documents.Commands;

/// <summary>How much of one invoiced line to credit.</summary>
/// <param name="LineId">The line on the original document.</param>
/// <param name="Quantity">How much of it to credit, in the unit it was invoiced in.</param>
public sealed record CreditLine(Guid LineId, decimal Quantity);

/// <summary>
/// Drafts a credit note against an issued document.
/// <para>
/// A draft, like every other document here, and for the same reason: issuing takes a number out
/// of a gapless sequence and a credit note is somebody giving money back, which is worth a second
/// pair of eyes even more than an invoice is.
/// </para>
/// </summary>
/// <param name="InvoiceId">The document being credited.</param>
/// <param name="Reason">Why. It goes against every line in the SAF-T export.</param>
/// <param name="Lines">
/// Which lines to credit and how much of each. Leave empty to credit everything still creditable,
/// which is the common case — the customer sends the lot back, or the invoice went to the wrong
/// company.
/// </param>
/// <param name="DocumentDate">The date on the credit note. Defaults to today.</param>
public sealed record DraftCreditNoteCommand(
    Guid InvoiceId,
    string Reason,
    IReadOnlyList<CreditLine>? Lines = null,
    DateOnly? DocumentDate = null) : ICommand<Guid>;

/// <summary>Checks the shape of a <see cref="DraftCreditNoteCommand"/>.</summary>
public sealed class DraftCreditNoteCommandValidator : IValidator<DraftCreditNoteCommand>
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(
        DraftCreditNoteCommand instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();

        if (instance.InvoiceId == Guid.Empty)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.InvoiceId), "required", "Say which document is being credited."));
        }

        if (string.IsNullOrWhiteSpace(instance.Reason))
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Reason), "required", "Say why the credit is being raised."));
        }

        // Two entries for one line is not a quantity of two; it is a caller who has lost track,
        // and silently adding them together would be this system deciding what they meant.
        if (instance.Lines is { Count: > 0 } lines
            && lines.Select(line => line.LineId).Distinct().Count() != lines.Count)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Lines), "duplicate_line",
                "The same line appears more than once. Give each line one quantity."));
        }

        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

/// <summary>Drafts the credit note.</summary>
public sealed class DraftCreditNoteCommandHandler : ICommandHandler<DraftCreditNoteCommand, Guid>
{
    private readonly IInvoiceRepository _invoices;
    private readonly IInvoicingUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the handler.</summary>
    public DraftCreditNoteCommandHandler(
        IInvoiceRepository invoices,
        IInvoicingUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _invoices = invoices;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> HandleAsync(
        DraftCreditNoteCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Invoice? original = await _invoices
            .GetByIdAsync(new InvoiceId(request.InvoiceId), cancellationToken)
            .ConfigureAwait(false);

        if (original is null)
        {
            return Result.Failure<Guid>(
                InvoicingErrors.Document.NotFound(request.InvoiceId.ToString()));
        }

        Result<Dictionary<InvoiceLineId, Quantity>> quantities = BuildQuantities(request, original);

        if (quantities.IsFailure)
        {
            return Result.Failure<Guid>(quantities.Error);
        }

        Result<Invoice> note = Invoice.DraftCreditNote(
            original,
            quantities.Value,
            request.Reason,
            request.DocumentDate ?? _clock.TodayUtc);

        if (note.IsFailure)
        {
            return Result.Failure<Guid>(note.Error);
        }

        _invoices.Add(note.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return note.Value.Id.Value;
    }

    private static Result<Dictionary<InvoiceLineId, Quantity>> BuildQuantities(
        DraftCreditNoteCommand request,
        Invoice original)
    {
        var quantities = new Dictionary<InvoiceLineId, Quantity>();

        if (request.Lines is not { Count: > 0 } lines)
        {
            return quantities;
        }

        foreach (CreditLine requested in lines)
        {
            var lineId = new InvoiceLineId(requested.LineId);
            InvoiceLine? line = original.Lines.FirstOrDefault(candidate => candidate.Id == lineId);

            if (line is null)
            {
                return Result.Failure<Dictionary<InvoiceLineId, Quantity>>(
                    InvoicingErrors.Credit.LineNotOnOriginal(requested.LineId.ToString()));
            }

            // The unit comes from the invoiced line rather than from the caller, because there is
            // exactly one unit a line can be credited in and asking for it again is only an
            // opportunity to disagree.
            Result<Quantity> quantity = Quantity.Create(requested.Quantity, line.Quantity.Unit);

            if (quantity.IsFailure)
            {
                return Result.Failure<Dictionary<InvoiceLineId, Quantity>>(quantity.Error);
            }

            quantities[lineId] = quantity.Value;
        }

        return quantities;
    }
}
