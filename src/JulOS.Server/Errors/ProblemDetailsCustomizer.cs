namespace JulOS.Server.Errors;

using JulOS.Application.Concurrency;
using JulOS.Contracts.Errors;
using JulOS.Domain.Primitives;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

/// <summary>Applies the public JulOS failure contract to every Problem Details response.</summary>
internal sealed partial class ProblemDetailsCustomizer
{
    private readonly ILogger<ProblemDetailsCustomizer> logger;

    public ProblemDetailsCustomizer(ILogger<ProblemDetailsCustomizer> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        this.logger = logger;
    }

    public void Customize(ProblemDetailsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var exception = context.HttpContext.Features.Get<IExceptionHandlerFeature>()?.Error;
        var correlationId = CorrelationIdentifier.Resolve(context.HttpContext);
        var classification = Classify(context, exception);

        context.HttpContext.Response.StatusCode = classification.Status;
        context.ProblemDetails.Status = classification.Status;
        context.ProblemDetails.Title = classification.Title;
        context.ProblemDetails.Detail = classification.Detail;
        context.ProblemDetails.Instance = context.HttpContext.Request.Path.Value;
        context.ProblemDetails.Extensions[ProblemExtensionNames.Code] = classification.Code;
        context.ProblemDetails.Extensions[ProblemExtensionNames.CorrelationId] = correlationId;
        context.ProblemDetails.Extensions[ProblemExtensionNames.Retryable] = classification.Retryable;

        if (exception is ConcurrencyConflictException { CurrentRevision: int currentRevision })
        {
            context.ProblemDetails.Extensions[ProblemExtensionNames.CurrentRevision] = currentRevision;
        }

        context.HttpContext.Response.Headers[CorrelationIdentifier.HeaderName] = correlationId;
        context.HttpContext.TraceIdentifier = correlationId;

        if (exception is not null)
        {
            LogUnhandledFailure(
                this.logger,
                correlationId,
                context.HttpContext.Request.Method,
                context.HttpContext.Request.Path.Value ?? string.Empty,
                exception);
        }
    }

    private static FailureClassification Classify(
        ProblemDetailsContext context,
        Exception? exception)
    {
        return exception switch
        {
            DomainRuleViolationException domainFailure => new(
                domainFailure.Code,
                "The request violates a platform rule.",
                StatusCodes.Status400BadRequest,
                domainFailure.Message,
                Retryable: false),

            ConcurrencyConflictException => new(
                PlatformErrorCodes.ConcurrencyConflict,
                "The resource changed.",
                StatusCodes.Status409Conflict,
                "Refresh the resource before retrying the intended change.",
                Retryable: false),

            _ when context.ProblemDetails.Status == StatusCodes.Status404NotFound => new(
                PlatformErrorCodes.NotFound,
                "The requested resource was not found.",
                StatusCodes.Status404NotFound,
                "The requested endpoint or resource does not exist.",
                Retryable: false),

            _ when context.ProblemDetails.Status is >= 400 and < 500 => new(
                PlatformErrorCodes.InvalidRequest,
                "The request could not be processed.",
                context.ProblemDetails.Status.Value,
                "Review the request and try again.",
                Retryable: false),

            _ => new(
                PlatformErrorCodes.InternalError,
                "The server could not complete the request.",
                StatusCodes.Status500InternalServerError,
                "Use the correlation ID to locate the server-side failure.",
                Retryable: false),
        };
    }

    [LoggerMessage(
        EventId = 1500,
        Level = LogLevel.Error,
        Message = "Request failed. CorrelationId={CorrelationId} Method={Method} Path={Path}")]
    private static partial void LogUnhandledFailure(
        ILogger logger,
        string correlationId,
        string method,
        string path,
        Exception exception);

    private sealed record FailureClassification(
        string Code,
        string Title,
        int Status,
        string Detail,
        bool Retryable);
}
