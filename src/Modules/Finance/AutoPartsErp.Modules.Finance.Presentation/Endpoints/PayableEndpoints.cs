using AutoPartsErp.Modules.Abstractions.Http;
using AutoPartsErp.Modules.Abstractions.Modules;
using AutoPartsErp.Modules.Finance.Application.Payments.Commands;
using AutoPartsErp.SharedKernel.Authorization;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AutoPartsErp.Modules.Finance.Presentation.Endpoints;

/// <summary>
/// HTTP routes for what the company owes its suppliers, and the money it sends them.
/// <para>
/// There is no route that creates a payable. They arrive from Purchasing when a supplier's invoice
/// is accepted, and a route that let somebody type one would be a way of owing money no document
/// stands behind.
/// </para>
/// </summary>
public sealed class PayableEndpoints : IEndpointGroup
{
    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapPost("/supplier-payments", RecordPaymentAsync)
            .WithName("RecordSupplierPayment")
            .RequirePermission(Permissions.Finance.PaySupplier)
            .WithSummary(
                "Record money that left. The allocations are optional: a transfer goes out on "
                + "Friday against a statement, and which documents it covered is answered on "
                + "Monday with the remittance in hand.")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/supplier-payments/{supplierPaymentId:guid}/allocations", AllocateAsync)
            .WithName("AllocateSupplierPayment")
            .RequirePermission(Permissions.Finance.PaySupplier)
            .WithSummary(
                "Say what a payment paid, once somebody has worked it out. All the lines or none "
                + "of them.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> RecordPaymentAsync(
        IDispatcher dispatcher,
        RecordSupplierPaymentCommand command,
        CancellationToken cancellationToken)
    {
        Result<Guid> result = await dispatcher.SendAsync(command, cancellationToken);

        return result.ToCreated(id => $"/api/finance/supplier-payments/{id}");
    }

    private static async Task<IResult> AllocateAsync(
        IDispatcher dispatcher,
        Guid supplierPaymentId,
        AllocatePaymentRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result result = await dispatcher.SendAsync(
            new AllocateSupplierPaymentCommand(supplierPaymentId, body.Allocations ?? []),
            cancellationToken);

        return result.ToNoContent();
    }
}

/// <summary>Body of a request that says what a payment paid.</summary>
/// <param name="Allocations">Which documents it paid, and how much of each.</param>
public sealed record AllocatePaymentRequest(IReadOnlyList<PaymentAllocationInput>? Allocations);
