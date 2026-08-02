using JulOS.Application.Authentication;
using JulOS.Application.Authorization;
using JulOS.Application.Concurrency;
using JulOS.Application.Profile;
using JulOS.Contracts.Errors;
using JulOS.Domain;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;

namespace JulOS.Server.Errors;

/// <summary>
/// Turns every failing response into the JulOS problem shape.
/// </summary>
/// <remarks>
/// This runs for handled failures and for unhandled exceptions alike, so no failure path
/// can return a differently shaped body. It is a plain static method rather than a
/// lambda inside the composition root so that the mapping is directly testable.
/// </remarks>
internal static class ProblemDetailsCustomizer
{
    private const string ProblemTypePrefix = "https://os.juloc.de/problems/";

    /// <summary>Fills in the JulOS members of one problem response.</summary>
    internal static void Apply(ProblemDetailsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var status = context.ProblemDetails.Status ?? context.HttpContext.Response.StatusCode;
        var exception = context.HttpContext.Features.Get<IExceptionHandlerFeature>()?.Error;

        var (code, retryable) = Classify(status, exception);

        context.ProblemDetails.Status = status;
        context.ProblemDetails.Type = ProblemTypePrefix + code.Replace('.', '-').Replace('_', '-');
        context.ProblemDetails.Title ??= TitleFor(status);

        // An unhandled exception message can carry a connection string, a file path or a
        // credential, so nothing derived from it reaches the client. The correlation
        // identifier is how the caller and the server-side log entry are matched up.
        if (exception is DomainRuleViolationException ruleViolation)
        {
            context.ProblemDetails.Detail = ruleViolation.Message;
        }
        else if (exception is AuthenticationFailureException authenticationFailure)
        {
            context.ProblemDetails.Detail = authenticationFailure.Message;
        }
        else if (exception is AuthorizationAdministrationException authorizationFailure)
        {
            context.ProblemDetails.Detail = authorizationFailure.Message;
        }
        else if (exception is ProfileFailureException profileFailure)
        {
            context.ProblemDetails.Detail = profileFailure.Message;
        }
        else if (exception is not null)
        {
            context.ProblemDetails.Detail = null;
        }

        context.ProblemDetails.Extensions[ProblemExtensionNames.Code] = code;
        context.ProblemDetails.Extensions[ProblemExtensionNames.CorrelationId] =
            CorrelationId.Get(context.HttpContext);
        context.ProblemDetails.Extensions[ProblemExtensionNames.Retryable] = retryable;

        if (exception is ConcurrencyConflictException { CurrentRevision: int currentRevision })
        {
            context.ProblemDetails.Extensions[ProblemExtensionNames.CurrentRevision] = currentRevision;
        }
    }

    private static (string Code, bool Retryable) Classify(int status, Exception? exception)
    {
        if (exception is ConcurrencyConflictException)
        {
            return (PlatformErrorCodes.ConcurrencyConflict, false);
        }

        if (exception is AuthenticationFailureException authenticationFailure)
        {
            return (authenticationFailure.Code, false);
        }

        if (exception is AuthorizationAdministrationException authorizationFailure)
        {
            return (authorizationFailure.Code, false);
        }

        if (exception is DomainRuleViolationException ruleViolation)
        {
            return (ruleViolation.Code, false);
        }

        if (exception is ProfileFailureException profileFailure)
        {
            return (profileFailure.Code, false);
        }

        return status switch
        {
            StatusCodes.Status400BadRequest => (PlatformErrorCodes.Invalid, false),
            StatusCodes.Status401Unauthorized => (PlatformErrorCodes.Unauthenticated, false),
            StatusCodes.Status403Forbidden => (PlatformErrorCodes.Forbidden, false),
            StatusCodes.Status404NotFound => (PlatformErrorCodes.NotFound, false),
            StatusCodes.Status409Conflict => (PlatformErrorCodes.RuleViolation, false),
            StatusCodes.Status429TooManyRequests => (PlatformErrorCodes.RateLimited, true),
            StatusCodes.Status503ServiceUnavailable => (PlatformErrorCodes.Unexpected, true),
            _ when status >= StatusCodes.Status500InternalServerError => (PlatformErrorCodes.Unexpected, false),
            _ => (PlatformErrorCodes.Invalid, false),
        };
    }

    private static string TitleFor(int status)
    {
        return status switch
        {
            StatusCodes.Status400BadRequest => "The request is invalid.",
            StatusCodes.Status401Unauthorized => "Authentication is required.",
            StatusCodes.Status403Forbidden => "The request is not permitted.",
            StatusCodes.Status404NotFound => "The resource does not exist.",
            StatusCodes.Status409Conflict => "The request conflicts with the current state.",
            StatusCodes.Status429TooManyRequests => "Too many requests were submitted.",
            StatusCodes.Status503ServiceUnavailable => "A required dependency is unavailable.",
            _ => "The request failed.",
        };
    }
}
