using MediPOS.Application.Errors;
using MediPOS.SharedKernel;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace MediPOS.Api.Errors;

public sealed class ApplicationExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApplicationExceptionHandler> logger) : IExceptionHandler
{
    private static readonly object TraceKey = new();
    private static readonly Action<ILogger, string, string, Exception?> LogUnexpected = LoggerMessage.Define<string, string>(
        LogLevel.Error, new EventId(1, "UnexpectedFailure"), "Unexpected failure of type {ExceptionType}; trace {TraceId}.");

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var traceId = GetTraceId(httpContext);
        var failure = exception as ApplicationErrorException;
        var status = failure?.Error.Category switch
        {
            ErrorCategory.Validation => StatusCodes.Status400BadRequest,
            ErrorCategory.Forbidden => StatusCodes.Status403Forbidden,
            ErrorCategory.NotFound => StatusCodes.Status404NotFound,
            ErrorCategory.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError,
        };
        if (status == StatusCodes.Status500InternalServerError)
            LogUnexpected(logger, exception.GetType().Name, traceId, null);
        var problem = new ProblemDetails
        {
            Status = status,
            Title = status == StatusCodes.Status500InternalServerError ? "An unexpected error occurred." : failure!.Error.Message,
        };
        problem.Extensions["code"] = status == StatusCodes.Status500InternalServerError ? "server.unexpected" : failure!.Error.Code;
        problem.Extensions["traceId"] = traceId;
        httpContext.Response.StatusCode = status;
        if (!await problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem }).ConfigureAwait(false))
            await httpContext.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken).ConfigureAwait(false);
        return true;
    }

    internal static string GetTraceId(HttpContext context)
    {
        if (context.Items.TryGetValue(TraceKey, out var existing) && existing is string trace)
            return trace;
        var created = ServerCorrelation.GetId();
        context.Items[TraceKey] = created;
        return created;
    }
}
