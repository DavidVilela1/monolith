using AutoPartsErp.Modules.Abstractions.Http;
using AutoPartsErp.Modules.Abstractions.Modules;
using AutoPartsErp.Modules.Purchasing.Application.Agreements.Commands;
using AutoPartsErp.Modules.Purchasing.Application.Invoices.Commands;
using AutoPartsErp.SharedKernel.Authorization;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AutoPartsErp.Modules.Purchasing.Presentation.Endpoints;

/// <summary>
/// HTTP routes for what was agreed with a supplier, and for their own documents.
/// <para>
/// There is no route that creates a supplier invoice. That is the point of the feature: documents
/// are written by the warehouse counting a pallet, and the only thing a person adds afterwards is
/// what the supplier's paper says.
/// </para>
/// </summary>
public sealed class SupplierAgreementEndpoints : IEndpointGroup
{
    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapPost("/agreements", OpenAgreementAsync)
            .WithName("OpenSupplierAgreement")
            .RequirePermission(Permissions.Purchasing.ManageAgreements)
            .WithSummary("Open an agreement with a supplier. No rebate until one is set.")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/agreements/{agreementId:guid}/rebate", SetRebateAsync)
            .WithName("SetSupplierRebate")
            .RequirePermission(Permissions.Purchasing.ManageAgreements)
            .WithSummary(
                "Set the rebate. OnInvoice takes it off their document; PeriodCreditNote leaves "
                + "the document alone and the money arrives afterwards. One step from zero is a "
                + "flat percentage.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/agreements/{agreementId:guid}/end", EndAgreementAsync)
            .WithName("EndSupplierAgreement")
            .RequirePermission(Permissions.Purchasing.ManageAgreements)
            .WithSummary("End an agreement. Deliveries after the last day price themselves at the order's figure.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/supplier-prices", AgreePriceAsync)
            .WithName("AgreeSupplierPrice")
            .RequirePermission(Permissions.Purchasing.ManageAgreements)
            .WithSummary(
                "Record what a supplier charges for a part from a day. A rise is a second call "
                + "with a later day, never an edit: both rows explain their own deliveries.")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/supplier-prices/{supplierPriceId:guid}", CorrectPriceAsync)
            .WithName("CorrectSupplierPrice")
            .RequirePermission(Permissions.Purchasing.ManageAgreements)
            .WithSummary("Correct a price that was typed wrong. For a mistake, not for a change.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPut("/supplier-invoices/{supplierInvoiceId:guid}/reconcile", ReconcileAsync)
            .WithName("ReconcileSupplierInvoice")
            .RequirePermission(Permissions.Purchasing.ManageInvoices)
            .WithSummary(
                "Record what their paper says: number, date and total. It settles if the two "
                + "totals agree within the rounding, and goes into dispute if they do not.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/supplier-invoices/{supplierInvoiceId:guid}/accept-their-figure", AcceptFigureAsync)
            .WithName("AcceptSupplierFigure")
            .RequirePermission(Permissions.Purchasing.ManageInvoices)
            .WithSummary(
                "Accept a disputed figure and owe it. Needs a reason: in a year it is the only "
                + "thing that explains paying more than was counted at prices that were agreed.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/supplier-invoices/{supplierInvoiceId:guid}/reopen", ReopenAsync)
            .WithName("ReopenSupplierInvoice")
            .RequirePermission(Permissions.Purchasing.ManageInvoices)
            .WithSummary("Send a disputed document back to draft so a line can be corrected.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPut("/supplier-invoices/{supplierInvoiceId:guid}/lines/{lineId:guid}/price", AcceptLinePriceAsync)
            .WithName("AcceptSupplierLinePrice")
            .RequirePermission(Permissions.Purchasing.ManageInvoices)
            .WithSummary(
                "Take their price on one line. Does not touch the agreed price — a supplier who "
                + "really raised their prices gets a new agreed price with a date on it.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/supplier-invoices/{supplierInvoiceId:guid}/cancel", CancelAsync)
            .WithName("CancelSupplierInvoice")
            .RequirePermission(Permissions.Purchasing.ManageInvoices)
            .WithSummary("Withdraw a draft opened by mistake. A settled document is credited, not cancelled.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> OpenAgreementAsync(
        IDispatcher dispatcher,
        OpenSupplierAgreementCommand command,
        CancellationToken cancellationToken)
    {
        Result<Guid> result = await dispatcher.SendAsync(command, cancellationToken);

        return result.ToCreated(id => $"/api/purchasing/agreements/{id}");
    }

    private static async Task<IResult> SetRebateAsync(
        IDispatcher dispatcher,
        Guid agreementId,
        SetRebateRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result result = await dispatcher.SendAsync(
            new SetSupplierRebateCommand(agreementId, body.Basis, body.Period, body.Steps ?? []),
            cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> EndAgreementAsync(
        IDispatcher dispatcher,
        Guid agreementId,
        EndAgreementRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result result = await dispatcher.SendAsync(
            new EndSupplierAgreementCommand(agreementId, body.LastDay, body.Note),
            cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> AgreePriceAsync(
        IDispatcher dispatcher,
        AgreeSupplierPriceCommand command,
        CancellationToken cancellationToken)
    {
        Result<Guid> result = await dispatcher.SendAsync(command, cancellationToken);

        return result.ToCreated(id => $"/api/purchasing/supplier-prices/{id}");
    }

    private static async Task<IResult> CorrectPriceAsync(
        IDispatcher dispatcher,
        Guid supplierPriceId,
        CorrectPriceRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result result = await dispatcher.SendAsync(
            new CorrectSupplierPriceCommand(supplierPriceId, body.UnitPrice, body.Note),
            cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> ReconcileAsync(
        IDispatcher dispatcher,
        Guid supplierInvoiceId,
        ReconcileRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result result = await dispatcher.SendAsync(
            new ReconcileSupplierInvoiceCommand(
                supplierInvoiceId, body.DocumentNumber, body.DocumentDate, body.StatedGrossTotal),
            cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> AcceptFigureAsync(
        IDispatcher dispatcher,
        Guid supplierInvoiceId,
        ReasonRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result result = await dispatcher.SendAsync(
            new AcceptSupplierFigureCommand(supplierInvoiceId, body.Reason), cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> ReopenAsync(
        IDispatcher dispatcher,
        Guid supplierInvoiceId,
        ReasonRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result result = await dispatcher.SendAsync(
            new ReopenSupplierInvoiceCommand(supplierInvoiceId, body.Reason), cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> AcceptLinePriceAsync(
        IDispatcher dispatcher,
        Guid supplierInvoiceId,
        Guid lineId,
        LinePriceRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result result = await dispatcher.SendAsync(
            new AcceptSupplierLinePriceCommand(supplierInvoiceId, lineId, body.UnitPrice),
            cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> CancelAsync(
        IDispatcher dispatcher,
        Guid supplierInvoiceId,
        ReasonRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result result = await dispatcher.SendAsync(
            new CancelSupplierInvoiceCommand(supplierInvoiceId, body.Reason), cancellationToken);

        return result.ToNoContent();
    }
}

/// <summary>Body of a request that sets a supplier's rebate.</summary>
/// <param name="Basis">OnInvoice, PeriodCreditNote or None.</param>
/// <param name="Period">Monthly, Quarterly or Annual. Required for a credit-note rebate.</param>
/// <param name="Steps">The steps. One step from zero is a flat percentage.</param>
public sealed record SetRebateRequest(
    string Basis,
    string? Period,
    IReadOnlyList<RebateStepInput>? Steps);

/// <summary>Body of a request that ends an agreement.</summary>
/// <param name="LastDay">The last day it applies, inclusive.</param>
/// <param name="Note">Why it ended.</param>
public sealed record EndAgreementRequest(DateOnly LastDay, string? Note);

/// <summary>Body of a request that corrects an agreed price.</summary>
/// <param name="UnitPrice">The figure it should have said.</param>
/// <param name="Note">Why it changed.</param>
public sealed record CorrectPriceRequest(decimal UnitPrice, string? Note);

/// <summary>Body of a request that records what a supplier's paper says.</summary>
/// <param name="DocumentNumber">Their document number, as printed.</param>
/// <param name="DocumentDate">The date on their document.</param>
/// <param name="StatedGrossTotal">What their document says the whole thing comes to.</param>
public sealed record ReconcileRequest(
    string DocumentNumber,
    DateOnly DocumentDate,
    decimal StatedGrossTotal);


/// <summary>Body of a request that takes the supplier's price on one line.</summary>
/// <param name="UnitPrice">What their document says one unit costs.</param>
public sealed record LinePriceRequest(decimal UnitPrice);
