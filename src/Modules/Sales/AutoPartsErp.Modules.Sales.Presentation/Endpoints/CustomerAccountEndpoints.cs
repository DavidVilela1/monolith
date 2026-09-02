using AutoPartsErp.Modules.Abstractions.Http;
using AutoPartsErp.Modules.Abstractions.Modules;
using AutoPartsErp.Modules.Sales.Application.Contracts;
using AutoPartsErp.Modules.Sales.Application.Orders.Queries;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Paging;
using AutoPartsErp.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AutoPartsErp.Modules.Sales.Presentation.Endpoints;

/// <summary>
/// HTTP routes for what Sales knows about its customers.
/// <para>
/// Read-only, and that is the whole design. Nothing here creates or edits an account: they are
/// opened, held, released and closed by Partners, and arrive as events. A POST on this path would
/// be a second place for the truth to live.
/// </para>
/// </summary>
public sealed class CustomerAccountEndpoints : IEndpointGroup
{
    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/customers", SearchAsync)
            .WithName("SearchCustomerAccounts")
            .WithSummary("Search customer accounts by code or name.")
            .Produces<PagedResult<CustomerAccountDto>>();

        group.MapGet("/customers/{customerId:guid}", GetAsync)
            .WithName("GetCustomerAccount")
            .WithSummary("Get one account with its limit, exposure and remaining credit.")
            .Produces<CustomerAccountDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/customers/by-code/{code}", GetByCodeAsync)
            .WithName("GetCustomerAccountByCode")
            .WithSummary("Get one account by the code typed at the counter.")
            .Produces<CustomerAccountDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> SearchAsync(
        IDispatcher dispatcher,
        string? term,
        string? status,
        int page = 1,
        int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        Result<PagedResult<CustomerAccountDto>> result = await dispatcher.SendAsync(
            new SearchCustomerAccountsQuery(term, status, page, pageSize),
            cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> GetAsync(
        IDispatcher dispatcher,
        Guid customerId,
        CancellationToken cancellationToken)
    {
        Result<CustomerAccountDto> result =
            await dispatcher.SendAsync(new GetCustomerAccountQuery(customerId), cancellationToken);

        return result.ToOk();
    }

    private static async Task<IResult> GetByCodeAsync(
        IDispatcher dispatcher,
        string code,
        CancellationToken cancellationToken)
    {
        Result<CustomerAccountDto> result =
            await dispatcher.SendAsync(new GetCustomerAccountByCodeQuery(code), cancellationToken);

        return result.ToOk();
    }
}
