using System.Diagnostics;
using AutoPartsErp.SharedKernel.Results;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AutoPartsErp.Modules.Abstractions.Http;

/// <summary>
/// Turns a <see cref="Result"/> into an HTTP response.
/// <para>
/// One translation table, used by every endpoint in every module, so that a conflict is always
/// a 409 and a broken domain rule is always a 422 no matter who wrote the endpoint. Clients get
/// RFC 7807 problem details with the stable error code in an <c>errorCode</c> extension, which
/// is what lets a front end react to a specific failure instead of matching on message text.
/// </para>
/// </summary>
public static class ResultExtensions
{
    /// <summary>Maps a failed result to a problem response. Never call this on a success.</summary>
    /// <exception cref="InvalidOperationException">The result is a success.</exception>
    public static IResult ToProblem(this Result result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsSuccess)
        {
            throw new InvalidOperationException("A successful result cannot be turned into a problem response.");
        }

        Error error = result.Error;

        if (error is ValidationError validation)
        {
            Dictionary<string, string[]> errors = validation.Failures
                .GroupBy(failure => failure.PropertyName, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(failure => failure.Message).ToArray(),
                    StringComparer.Ordinal);

            return Results.ValidationProblem(
                errors,
                detail: validation.Description,
                title: "One or more fields are invalid.",
                extensions: new Dictionary<string, object?> { ["errorCode"] = error.Code });
        }

        (int statusCode, string title) = Describe(error.Type);

        return Results.Problem(
            detail: error.Description,
            statusCode: statusCode,
            title: title,
            extensions: new Dictionary<string, object?> { ["errorCode"] = error.Code });
    }

    /// <summary>Returns 200 with the value on success, or the mapped problem on failure.</summary>
    public static IResult ToOk<TValue>(this Result<TValue> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
    }

    /// <summary>Returns 200 with a projected value on success, or the mapped problem on failure.</summary>
    public static IResult ToOk<TValue, TOut>(this Result<TValue> result, Func<TValue, TOut> project)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(project);
        return result.IsSuccess ? Results.Ok(project(result.Value)) : result.ToProblem();
    }

    /// <summary>Returns 204 on success, or the mapped problem on failure.</summary>
    public static IResult ToNoContent(this Result result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsSuccess ? Results.NoContent() : result.ToProblem();
    }

    /// <summary>Returns 201 with a Location header on success, or the mapped problem on failure.</summary>
    /// <param name="result">The outcome of the create.</param>
    /// <param name="location">Builds the URI of the newly created resource.</param>
    public static IResult ToCreated<TValue>(this Result<TValue> result, Func<TValue, string> location)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(location);

        return result.IsSuccess
            ? Results.Created(location(result.Value), result.Value)
            : result.ToProblem();
    }

    /// <summary>Maps a failure classification to its status code and title.</summary>
    public static (int StatusCode, string Title) Describe(ErrorType type) => type switch
    {
        ErrorType.Validation => (StatusCodes.Status400BadRequest, "The request is not valid."),
        ErrorType.NotFound => (StatusCodes.Status404NotFound, "The resource was not found."),
        ErrorType.Conflict => (StatusCodes.Status409Conflict, "The request conflicts with the current state."),

        // 422 rather than 400: the request was well formed, but a business rule forbids it.
        ErrorType.DomainRule => (StatusCodes.Status422UnprocessableEntity, "A business rule was violated."),
        ErrorType.Forbidden => (StatusCodes.Status403Forbidden, "You are not permitted to do that."),
        _ => (StatusCodes.Status500InternalServerError, "Something went wrong."),
    };
}

/// <summary>
/// Turns an unhandled exception into an RFC 7807 problem response, and logs it.
/// Registered as the last line of defence: expected failures never reach here, so anything
/// that does is a genuine defect worth an error-level log entry.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IHostEnvironment _environment;

    /// <summary>Initializes the handler.</summary>
    public GlobalExceptionHandler(
        ILogger<GlobalExceptionHandler> logger,
        IHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
    }

    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        _logger.LogError(
            exception,
            "Unhandled exception on {Method} {Path}",
            httpContext.Request.Method,
            httpContext.Request.Path);

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Something went wrong.",

            // Stack traces are useful in development and a gift to an attacker in production.
            Detail = _environment.IsDevelopment()
                ? exception.ToString()
                : "An unexpected error occurred. The incident has been logged.",
            Instance = httpContext.Request.Path,
        };

        problem.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken).ConfigureAwait(false);

        return true;
    }
}
