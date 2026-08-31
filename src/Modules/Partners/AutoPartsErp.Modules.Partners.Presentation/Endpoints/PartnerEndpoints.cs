using AutoPartsErp.Modules.Abstractions.Http;
using AutoPartsErp.Modules.Abstractions.Modules;
using AutoPartsErp.Modules.Partners.Application.Commands;
using AutoPartsErp.Modules.Partners.Application.Contracts;
using AutoPartsErp.Modules.Partners.Application.Queries;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Paging;
using AutoPartsErp.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AutoPartsErp.Modules.Partners.Presentation.Endpoints;

/// <summary>HTTP routes for customers and suppliers.</summary>
public sealed class PartnerEndpoints : IEndpointGroup
{
    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/", SearchAsync)
            .WithName("SearchPartners")
            .WithSummary("Search partners by code, name or tax number.")
            .Produces<PagedResult<PartnerSummary>>();

        group.MapGet("/{partnerId:guid}", GetAsync)
            .WithName("GetPartner")
            .WithSummary("Get one partner with addresses, contacts and terms.")
            .Produces<PartnerDetail>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/by-code/{code}", GetByCodeAsync)
            .WithName("GetPartnerByCode")
            .WithSummary("Get one partner by their short code.")
            .Produces<PartnerDetail>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .WithName("CreatePartner")
            .WithSummary("Register a partner. Roles are granted separately.")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{partnerId:guid}/addresses", AddAddressAsync)
            .WithName("AddPartnerAddress")
            .WithSummary("Record an address. A partner has one billing address and many delivery ones.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();

        group.MapPost("/{partnerId:guid}/contacts", AddContactAsync)
            .WithName("AddPartnerContact")
            .WithSummary("Record someone to contact there.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();

        group.MapPut("/{partnerId:guid}/customer-role", GrantCustomerAsync)
            .WithName("GrantCustomerRole")
            .WithSummary("Start selling to them. Requires a billing address.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPut("/{partnerId:guid}/supplier-role", GrantSupplierAsync)
            .WithName("GrantSupplierRole")
            .WithSummary("Start buying from them.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();

        group.MapPost("/{partnerId:guid}/hold", HoldAsync)
            .WithName("PlacePartnerOnHold")
            .WithSummary("Stop new orders. A reason is required.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();

        group.MapDelete("/{partnerId:guid}/hold", ReleaseHoldAsync)
            .WithName("ReleasePartnerHold")
            .WithSummary("Lift a hold.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> SearchAsync(
        IDispatcher dispatcher,
        string? term,
        bool? isCustomer,
        bool? isSupplier,
        string? status,
        int page = 1,
        int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        Result<PagedResult<PartnerSummary>> result = await dispatcher.SendAsync(
            new SearchPartnersQuery(term, isCustomer, isSupplier, status, page, pageSize),
            cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> GetAsync(
        IDispatcher dispatcher,
        Guid partnerId,
        CancellationToken cancellationToken)
    {
        Result<PartnerDetail> result =
            await dispatcher.SendAsync(new GetPartnerQuery(partnerId), cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> GetByCodeAsync(
        IDispatcher dispatcher,
        string code,
        CancellationToken cancellationToken)
    {
        Result<PartnerDetail> result =
            await dispatcher.SendAsync(new GetPartnerByCodeQuery(code), cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> CreateAsync(
        IDispatcher dispatcher,
        CreatePartnerCommand command,
        CancellationToken cancellationToken)
    {
        Result<Guid> result = await dispatcher.SendAsync(command, cancellationToken);
        return result.ToCreated(id => $"/api/partners/{id}");
    }

    private static async Task<IResult> AddAddressAsync(
        IDispatcher dispatcher,
        Guid partnerId,
        AddAddressRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result result = await dispatcher.SendAsync(
            new AddPartnerAddressCommand(
                partnerId, body.Kind, body.Line1, body.Postcode, body.City,
                body.CountryCode, body.Line2, body.Notes),
            cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> AddContactAsync(
        IDispatcher dispatcher,
        Guid partnerId,
        AddContactRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result result = await dispatcher.SendAsync(
            new AddPartnerContactCommand(
                partnerId, body.Name, body.Role, body.Email, body.Phone, body.IsPrimary),
            cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> GrantCustomerAsync(
        IDispatcher dispatcher,
        Guid partnerId,
        GrantCustomerRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result result = await dispatcher.SendAsync(
            new GrantCustomerRoleCommand(
                partnerId, body.CreditLimit, body.CurrencyCode, body.PaymentDueInDays,
                body.PaymentMethod, body.EndOfMonth, body.PriceListCode),
            cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> GrantSupplierAsync(
        IDispatcher dispatcher,
        Guid partnerId,
        GrantSupplierRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result result = await dispatcher.SendAsync(
            new GrantSupplierRoleCommand(
                partnerId, body.PaymentDueInDays, body.PaymentMethod, body.LeadTimeDays,
                body.EndOfMonth, body.MinimumOrderValue, body.CurrencyCode, body.OurAccountNumber),
            cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> HoldAsync(
        IDispatcher dispatcher,
        Guid partnerId,
        HoldRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result result = await dispatcher.SendAsync(
            new PlacePartnerOnHoldCommand(partnerId, body.Reason),
            cancellationToken);

        return result.ToNoContent();
    }

    private static async Task<IResult> ReleaseHoldAsync(
        IDispatcher dispatcher,
        Guid partnerId,
        CancellationToken cancellationToken)
    {
        Result result = await dispatcher.SendAsync(
            new ReleasePartnerHoldCommand(partnerId),
            cancellationToken);

        return result.ToNoContent();
    }
}

/// <summary>Body of an add-address request.</summary>
/// <param name="Kind">Billing, Delivery or Registered.</param>
/// <param name="Line1">Street and number.</param>
/// <param name="Postcode">Postcode.</param>
/// <param name="City">City or town.</param>
/// <param name="CountryCode">ISO two-letter country code.</param>
/// <param name="Line2">Optional second line.</param>
/// <param name="Notes">Optional delivery notes.</param>
public sealed record AddAddressRequest(
    string Kind,
    string Line1,
    string Postcode,
    string City,
    string CountryCode,
    string? Line2,
    string? Notes);

/// <summary>Body of an add-contact request.</summary>
/// <param name="Name">The person's name.</param>
/// <param name="Role">What they do.</param>
/// <param name="Email">Email address.</param>
/// <param name="Phone">Phone number.</param>
/// <param name="IsPrimary">True for the person to call by default.</param>
public sealed record AddContactRequest(
    string Name,
    string? Role,
    string? Email,
    string? Phone,
    bool IsPrimary);

/// <summary>Body of a grant-customer-role request.</summary>
/// <param name="CreditLimit">How much they may owe at once. Zero means cash only.</param>
/// <param name="CurrencyCode">Currency of the limit.</param>
/// <param name="PaymentDueInDays">Days to pay. Zero means on delivery.</param>
/// <param name="PaymentMethod">Cash, Card, BankTransfer, DirectDebit or Cheque.</param>
/// <param name="EndOfMonth">True to count from the end of the invoice month.</param>
/// <param name="PriceListCode">Which price list applies.</param>
public sealed record GrantCustomerRequest(
    decimal CreditLimit,
    string CurrencyCode,
    int PaymentDueInDays,
    string PaymentMethod,
    bool EndOfMonth,
    string? PriceListCode);

/// <summary>Body of a grant-supplier-role request.</summary>
/// <param name="PaymentDueInDays">Days we have to pay them.</param>
/// <param name="PaymentMethod">How we pay them.</param>
/// <param name="LeadTimeDays">Typical days from order to delivery.</param>
/// <param name="EndOfMonth">True to count from month end.</param>
/// <param name="MinimumOrderValue">The value below which they will not ship.</param>
/// <param name="CurrencyCode">Currency of the minimum order value.</param>
/// <param name="OurAccountNumber">Our account number with them.</param>
public sealed record GrantSupplierRequest(
    int PaymentDueInDays,
    string PaymentMethod,
    int LeadTimeDays,
    bool EndOfMonth,
    decimal? MinimumOrderValue,
    string? CurrencyCode,
    string? OurAccountNumber);

/// <summary>Body of a hold request.</summary>
/// <param name="Reason">Why the account is being held.</param>
public sealed record HoldRequest(string Reason);
