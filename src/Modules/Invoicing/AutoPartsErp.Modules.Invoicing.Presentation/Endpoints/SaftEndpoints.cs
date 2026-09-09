using System.Text;
using AutoPartsErp.Modules.Abstractions.Http;
using AutoPartsErp.Modules.Abstractions.Modules;
using AutoPartsErp.Modules.Invoicing.Application.Saft;
using AutoPartsErp.SharedKernel.Authorization;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AutoPartsErp.Modules.Invoicing.Presentation.Endpoints;

/// <summary>
/// The SAF-T (PT) export.
/// <para>
/// A GET, because producing the file changes nothing — it is a rendering of documents that
/// already exist, and asking for it twice gives the same bytes. That matters more than it sounds:
/// the first thing anybody does with a file the tax authority rejected is fix something and ask
/// for it again, and an export that had side effects would make that a nervous act.
/// </para>
/// </summary>
public sealed class SaftEndpoints : IEndpointGroup
{
    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/saft", ExportAsync)
            .WithName("ExportSaft")
            .RequirePermission(Permissions.Invoicing.ExportSaft)
            .WithSummary(
                "Produce the SAF-T (PT) file for a period, as XML. Includes every issued document "
                + "in the period, voided ones included; excludes drafts, which have no number and "
                + "do not exist as far as the tax authority is concerned.")
            .Produces<string>(StatusCodes.Status200OK, "application/xml")
            .ProducesValidationProblem();

        group.MapGet("/saft/{year:int}/{month:int}", ExportMonthAsync)
            .WithName("ExportSaftForMonth")
            .RequirePermission(Permissions.Invoicing.ExportSaft)
            .WithSummary(
                "The same file for one calendar month, which is the period the monthly filing "
                + "actually wants and the one nobody should have to work out the end date of.")
            .Produces<string>(StatusCodes.Status200OK, "application/xml")
            .ProducesValidationProblem();
    }

    private static async Task<IResult> ExportAsync(
        IDispatcher dispatcher,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        Result<SaftExport> result =
            await dispatcher.SendAsync(new ExportSaftQuery(from, to), cancellationToken);

        return AsFile(result);
    }

    private static async Task<IResult> ExportMonthAsync(
        IDispatcher dispatcher,
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        if (month is < 1 or > 12 || year is < 2000 or > 2999)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["period"] = ["Give a year between 2000 and 2999 and a month between 1 and 12."],
            });
        }

        var from = new DateOnly(year, month, 1);
        DateOnly to = from.AddMonths(1).AddDays(-1);

        Result<SaftExport> result =
            await dispatcher.SendAsync(new ExportSaftQuery(from, to), cancellationToken);

        return AsFile(result);
    }

    private static IResult AsFile(Result<SaftExport> result)
    {
        if (result.IsFailure)
        {
            return result.ToProblem();
        }

        SaftExport export = result.Value;

        // UTF-8 with no byte-order mark. The declaration says UTF-8 and the validator reads it;
        // a BOM in front of the declaration is the kind of thing that produces a rejection
        // message about content before the root element, which tells nobody anything.
        byte[] bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            .GetBytes(export.Xml);

        return Results.File(bytes, "application/xml", export.FileName);
    }
}
