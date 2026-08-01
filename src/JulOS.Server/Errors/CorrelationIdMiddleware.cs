namespace JulOS.Server.Errors;

/// <summary>
/// Gives every request a correlation identifier and returns it to the caller.
/// </summary>
/// <remarks>
/// The identifier is written to the response before the pipeline continues, so it is
/// present even when a later stage fails, and it is pushed onto the logging scope so
/// every entry of the request carries it without each call site repeating it.
/// </remarks>
internal sealed class CorrelationIdMiddleware
{
    private readonly RequestDelegate next;

    private readonly ILogger<CorrelationIdMiddleware> logger;

    /// <summary>Creates the middleware.</summary>
    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(logger);

        this.next = next;
        this.logger = logger;
    }

    /// <summary>Runs the middleware for one request.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var correlationId = CorrelationId.Accept(context.Request.Headers[CorrelationId.HeaderName]);

        CorrelationId.Set(context, correlationId);
        context.Response.Headers[CorrelationId.HeaderName] = correlationId;

        using var scope = this.logger.BeginScope(
            new Dictionary<string, object> { ["CorrelationId"] = correlationId });

        await this.next(context).ConfigureAwait(false);
    }
}
