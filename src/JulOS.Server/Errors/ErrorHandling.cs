namespace JulOS.Server.Errors;

using JulOS.Application.Concurrency;
using JulOS.Domain.Primitives;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

/// <summary>Registers the single JulOS failure-response pipeline.</summary>
internal static class ErrorHandling
{
    internal static IServiceCollection AddJulOSProblemDetails(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = context =>
                context.HttpContext.RequestServices
                    .GetRequiredService<ProblemDetailsCustomizer>()
                    .Customize(context));
        services.AddSingleton<ProblemDetailsCustomizer>();
        return services;
    }

    internal static void UseJulOSExceptionHandler(this WebApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);

        application.UseExceptionHandler(new ExceptionHandlerOptions
        {
            StatusCodeSelector = exception => exception switch
            {
                DomainRuleViolationException => StatusCodes.Status400BadRequest,
                ConcurrencyConflictException => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status500InternalServerError,
            },
        });
    }
}
