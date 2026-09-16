using JulOS.Application.Authentication;
using JulOS.Application.Authorization;
using JulOS.Application.Catalog;
using JulOS.Application.Concurrency;
using JulOS.Application.Devices;
using JulOS.Application.Layouts;
using JulOS.Contracts.Devices;
using JulOS.Application.Profile;
using JulOS.Application.Operations;
using JulOS.Application.Secrets;
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
            OperationFailureException operation => operation.Reason switch
            {
                OperationFailureReason.Invalid => StatusCodes.Status400BadRequest,
                OperationFailureReason.NotFound => StatusCodes.Status404NotFound,
                OperationFailureReason.IdempotencyConflict => StatusCodes.Status409Conflict,
                OperationFailureReason.InvalidTransition => StatusCodes.Status409Conflict,
                OperationFailureReason.NotCancellable => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status500InternalServerError,
            },
            WorkspaceLayoutFailureException layout => layout.Reason switch
            {
                WorkspaceLayoutFailureReason.NotFound => StatusCodes.Status404NotFound,
                // Fresh mode is a state conflict rather than a malformed request: the same
                // document would be accepted once the workspace stops starting fresh.
                WorkspaceLayoutFailureReason.PersistenceDisabled => StatusCodes.Status409Conflict,
                WorkspaceLayoutFailureReason.PhoneForegroundLimitExceeded => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest,
            },
            ProfileFailureException profile => profile.Reason switch
            {
                ProfileFailureReason.InvalidPreferences => StatusCodes.Status400BadRequest,
                ProfileFailureReason.NotFound => StatusCodes.Status404NotFound,
                _ => StatusCodes.Status500InternalServerError,
            },
            SecretReferenceFailureException secret => secret.Reason switch
            {
                SecretReferenceFailureReason.Invalid => StatusCodes.Status400BadRequest,
                SecretReferenceFailureReason.NotFound => StatusCodes.Status404NotFound,
                SecretReferenceFailureReason.Deleted => StatusCodes.Status409Conflict,
                SecretReferenceFailureReason.LeaseDenied => StatusCodes.Status404NotFound,
                SecretReferenceFailureReason.Unavailable => StatusCodes.Status503ServiceUnavailable,
                SecretReferenceFailureReason.LeaseExpired => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status500InternalServerError,
            },
            ApplicationExecutionPreferenceException preference => preference.Code switch
            {
                ClientDeviceErrorCodes.NotFound => StatusCodes.Status404NotFound,
                ClientDeviceErrorCodes.NotOwned => StatusCodes.Status403Forbidden,
                _ => StatusCodes.Status400BadRequest,
            },
            ClientDeviceException device => device.Code switch
            {
                ClientDeviceErrorCodes.NotFound => StatusCodes.Status404NotFound,
                ClientDeviceErrorCodes.NotRegistered => StatusCodes.Status404NotFound,
                ClientDeviceErrorCodes.NotOwned => StatusCodes.Status403Forbidden,
                _ => StatusCodes.Status400BadRequest,
            },
            CatalogFailureException catalogFailure => catalogFailure.Reason switch
            {
                CatalogFailureReason.NotFound => StatusCodes.Status404NotFound,
                // Both are state conflicts: the same document would be accepted against a
                // live source, or against a location no other source is reading from.
                CatalogFailureReason.Removed => StatusCodes.Status409Conflict,
                CatalogFailureReason.Duplicate => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest,
            },
            ConcurrencyConflictException => StatusCodes.Status409Conflict,
            DomainRuleViolationException => StatusCodes.Status409Conflict,
            ArgumentException => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError,
        };
    }
}
