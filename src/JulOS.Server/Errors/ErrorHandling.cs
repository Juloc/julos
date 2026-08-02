using JulOS.Application.Authentication;
using JulOS.Application.Authorization;
using JulOS.Application.Concurrency;
using JulOS.Application.Profile;
using JulOS.Domain;

namespace JulOS.Server.Errors;

/// <summary>
/// Wires the single error-handling path of JulOS Server.
/// </summary>
internal static class ErrorHandling
{
    /// <summary>Registers the JulOS problem shape for every failing response.</summary>
    internal static IServiceCollection AddJulOsErrorHandling(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddProblemDetails(options => options.CustomizeProblemDetails = ProblemDetailsCustomizer.Apply);

        return services;
    }

    /// <summary>
    /// Installs correlation identifiers and the failure pipeline.
    /// </summary>
    /// <remarks>
    /// Order matters. Correlation runs first so that a failure handled further down still
    /// has an identifier to report. The developer exception page is never used, because a
    /// response shape that differs between environments hides exactly the production
    /// behaviour that needs testing.
    /// </remarks>
    internal static WebApplication UseJulOsErrorHandling(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseMiddleware<CorrelationIdMiddleware>();

        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            StatusCodeSelector = SelectStatusCode,
            AllowStatusCode404Response = true,
        });

        app.UseStatusCodePages();

        return app;
    }

    /// <summary>Maps an unhandled exception to the status code it deserves.</summary>
    private static int SelectStatusCode(Exception exception)
    {
        return exception switch
        {
            AuthenticationFailureException authentication => authentication.Reason switch
            {
                AuthenticationFailureReason.SetupAlreadyCompleted => StatusCodes.Status409Conflict,
                AuthenticationFailureReason.SetupRequired => StatusCodes.Status409Conflict,
                AuthenticationFailureReason.InvalidSetupRequest => StatusCodes.Status400BadRequest,
                AuthenticationFailureReason.InvalidCredentials => StatusCodes.Status401Unauthorized,
                AuthenticationFailureReason.AntiforgeryInvalid => StatusCodes.Status400BadRequest,
                _ => StatusCodes.Status500InternalServerError,
            },
            AuthorizationAdministrationException authorization => authorization.Reason switch
            {
                AuthorizationAdministrationFailureReason.InvalidRole => StatusCodes.Status400BadRequest,
                AuthorizationAdministrationFailureReason.InvalidAssignment => StatusCodes.Status400BadRequest,
                AuthorizationAdministrationFailureReason.RoleNotFound => StatusCodes.Status404NotFound,
                AuthorizationAdministrationFailureReason.UserNotFound => StatusCodes.Status404NotFound,
                AuthorizationAdministrationFailureReason.AssignmentNotFound => StatusCodes.Status404NotFound,
                AuthorizationAdministrationFailureReason.SystemRoleImmutable => StatusCodes.Status409Conflict,
                AuthorizationAdministrationFailureReason.LastAdministrator => StatusCodes.Status409Conflict,
                AuthorizationAdministrationFailureReason.DuplicateAssignment => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status500InternalServerError,
            },
            ProfileFailureException profile => profile.Reason switch
            {
                ProfileFailureReason.InvalidPreferences => StatusCodes.Status400BadRequest,
                ProfileFailureReason.NotFound => StatusCodes.Status404NotFound,
                _ => StatusCodes.Status500InternalServerError,
            },
            ConcurrencyConflictException => StatusCodes.Status409Conflict,
            DomainRuleViolationException => StatusCodes.Status409Conflict,
            ArgumentException => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError,
        };
    }
}
