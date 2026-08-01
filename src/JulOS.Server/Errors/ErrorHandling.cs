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
            DomainRuleViolationException => StatusCodes.Status409Conflict,
            ArgumentException => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError,
        };
    }
}
