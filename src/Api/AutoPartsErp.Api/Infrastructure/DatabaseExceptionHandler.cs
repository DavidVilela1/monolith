using System.Data.Common;
using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AutoPartsErp.Api.Infrastructure;

/// <summary>
/// Turns the database's way of saying "somebody got there first" into a 409.
/// <para>
/// Everything in this file is about the same class of event: two requests that would each have
/// been fine on their own, arriving close enough together that the second one loses. The use
/// cases already check for most of it — is this code taken, is this row still the version I
/// read — but a check and the write that follows it are two statements, and between them is a
/// window another request can occupy. The database closes that window with a constraint or a
/// concurrency token, and it closes it by throwing.
/// </para>
/// <para>
/// Left alone, all of that reached <c>GlobalExceptionHandler</c> and came back as 500 with an
/// error-level log entry. That is wrong twice over: it tells the caller the server is broken when
/// in fact their request simply lost a race and would succeed on a retry, and it fills the log
/// with alarms about a system working exactly as designed. A client cannot retry what it has been
/// told is a server fault.
/// </para>
/// <para>
/// Registered ahead of <c>GlobalExceptionHandler</c> and deliberately narrow: anything it does
/// not recognize falls through to be treated as the defect it probably is.
/// </para>
/// </summary>
public sealed class DatabaseExceptionHandler : IExceptionHandler
{
    /// <summary>PostgreSQL <c>unique_violation</c>: a second row with a value that must be unique.</summary>
    private const string UniqueViolation = "23505";

    /// <summary>PostgreSQL <c>foreign_key_violation</c>: pointing at a row that is not there any more.</summary>
    private const string ForeignKeyViolation = "23503";

    /// <summary>PostgreSQL <c>check_violation</c>: a row the table's own rules forbid.</summary>
    private const string CheckViolation = "23514";

    /// <summary>PostgreSQL <c>serialization_failure</c>: the transaction could not be ordered against another.</summary>
    private const string SerializationFailure = "40001";

    /// <summary>PostgreSQL <c>deadlock_detected</c>: two transactions waiting on each other, one chosen to die.</summary>
    private const string DeadlockDetected = "40P01";

    private readonly ILogger<DatabaseExceptionHandler> _logger;

    /// <summary>Initializes the handler.</summary>
    public DatabaseExceptionHandler(ILogger<DatabaseExceptionHandler> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        (int Status, string Title, string Detail, string Code)? problem = Classify(exception);

        if (problem is not { } response)
        {
            return false;
        }

        // Warning, not error. Somebody losing a race is not a defect; a lot of somebodies losing
        // races is a queue forming somewhere, which is worth seeing in a log without being woken
        // up for. The exception goes with it because the constraint name is in there and it is
        // the only thing that says which race this was.
        _logger.LogWarning(
            exception,
            "{Method} {Path} lost a database race: {ErrorCode}",
            httpContext.Request.Method,
            httpContext.Request.Path,
            response.Code);

        var details = new ProblemDetails
        {
            Status = response.Status,
            Title = response.Title,
            Detail = response.Detail,
            Instance = httpContext.Request.Path,
        };

        // The same extension every other failure in this system carries, so a front end matches
        // on one field whether the rule was enforced by a use case or by a constraint.
        details.Extensions["errorCode"] = response.Code;
        details.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = response.Status;

        await httpContext.Response
            .WriteAsJsonAsync(details, cancellationToken)
            .ConfigureAwait(false);

        return true;
    }

    private static (int Status, string Title, string Detail, string Code)? Classify(
        Exception exception)
    {
        if (exception is DbUpdateConcurrencyException)
        {
            return (
                StatusCodes.Status409Conflict,
                "The request conflicts with the current state.",
                "Somebody else changed this while you were working on it. Reload and try again.",
                "persistence.concurrency");
        }

        // Read through System.Data.Common rather than through Npgsql: SqlState has been on
        // DbException since .NET 5, and going through the base class keeps the composition root
        // from taking a direct dependency on the driver to read five string constants.
        string? sqlState = (exception as DbException ?? exception.InnerException as DbException)
            ?.SqlState;

        return sqlState switch
        {
            UniqueViolation => (
                StatusCodes.Status409Conflict,
                "The request conflicts with the current state.",
                "Something with that value already exists. It may have been created a moment ago "
                + "by somebody else.",
                "persistence.duplicate"),

            ForeignKeyViolation => (
                StatusCodes.Status409Conflict,
                "The request conflicts with the current state.",
                "This refers to something that no longer exists, or is still referred to by "
                + "something else.",
                "persistence.reference"),

            CheckViolation => (
                StatusCodes.Status422UnprocessableEntity,
                "A business rule was violated.",
                "The database refused the change as invalid.",
                "persistence.check"),

            // Both mean: nothing is wrong, this transaction was the one chosen to give way.
            // The honest answer is a 409 that says so, because the same request sent again
            // will very probably work.
            SerializationFailure or DeadlockDetected => (
                StatusCodes.Status409Conflict,
                "The request conflicts with the current state.",
                "The request collided with another one. Nothing was changed; try again.",
                "persistence.contention"),

            _ => null,
        };
    }
}
