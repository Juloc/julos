using System.Security.Cryptography;
using System.Text;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;

namespace JulOS.RuntimeManager;

/// <summary>Validated Runtime Manager authentication and network configuration.</summary>
/// <param name="ApiKey">Shared control-plane API key.</param>
/// <param name="AllowedNetworks">Container networks packages may request.</param>
public sealed record RuntimeManagerOptions(string ApiKey, IReadOnlyList<string> AllowedNetworks)
{
    /// <summary>Reads and validates Runtime Manager configuration.</summary>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>Validated options.</returns>
    public static RuntimeManagerOptions Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var apiKey = configuration["RuntimeManager:ApiKey"]
            ?? Environment.GetEnvironmentVariable("JULOS_RUNTIME_MANAGER_KEY")
            ?? throw new InvalidOperationException(
                "Runtime Manager API key is missing. Set RuntimeManager__ApiKey or JULOS_RUNTIME_MANAGER_KEY.");
        if (apiKey.Length < 32 || apiKey.Any(char.IsControl))
        {
            throw new InvalidOperationException("Runtime Manager API key must contain at least 32 non-control characters.");
        }

        var configuredNetworks = configuration.GetSection("RuntimeManager:AllowedNetworks").Get<string[]>()
            ?? (Environment.GetEnvironmentVariable("JULOS_RUNTIME_ALLOWED_NETWORKS") ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new RuntimeManagerOptions(apiKey, configuredNetworks);
    }
}

/// <summary>Authenticates every privileged Runtime Manager request in constant time.</summary>
public sealed class RuntimeManagerAuthenticationMiddleware
{
    private const string ApiKeyHeader = "X-JulOS-Runtime-Key";
    private readonly RequestDelegate next;
    private readonly byte[] expectedKey;

    /// <summary>Creates the authentication middleware.</summary>
    /// <param name="next">Next middleware delegate.</param>
    /// <param name="options">Validated Runtime Manager options.</param>
    public RuntimeManagerAuthenticationMiddleware(RequestDelegate next, RuntimeManagerOptions options)
    {
        this.next = next ?? throw new ArgumentNullException(nameof(next));
        ArgumentNullException.ThrowIfNull(options);
        this.expectedKey = Encoding.UTF8.GetBytes(options.ApiKey);
    }

    /// <summary>Authenticates one request before forwarding it.</summary>
    /// <param name="context">HTTP request context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Request.Path.StartsWithSegments("/health", StringComparison.Ordinal))
        {
            await this.next(context).ConfigureAwait(false);
            return;
        }

        var supplied = context.Request.Headers[ApiKeyHeader].ToString();
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        try
        {
            if (suppliedBytes.Length != this.expectedKey.Length
                || !CryptographicOperations.FixedTimeEquals(suppliedBytes, this.expectedKey))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(
                    new RuntimeError("runtime.authentication.required", "Runtime Manager authentication failed."),
                    context.RequestAborted).ConfigureAwait(false);
                return;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(suppliedBytes);
        }

        await this.next(context).ConfigureAwait(false);
    }
}

/// <summary>Maps the narrow authenticated Runtime Manager HTTP contract.</summary>
public static class RuntimeManagerEndpoints
{
    /// <summary>Maps liveness and managed-runtime endpoints.</summary>
    /// <param name="endpoints">Endpoint route builder.</param>
    /// <returns>The same builder.</returns>
    public static IEndpointRouteBuilder MapRuntimeManager(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapGet("/health/live", () => Results.Ok(new { status = "live" }));

        var group = endpoints.MapGroup("/v1/runtimes");
        group.MapPost(
            "/",
            (RuntimeCreateRequest request, IRuntimeBackend backend, CancellationToken cancellationToken) =>
                ExecuteAsync(() => backend.CreateAsync(request, cancellationToken)));
        group.MapGet(
            "/{runtimeId}",
            async (string runtimeId, IRuntimeBackend backend, CancellationToken cancellationToken) =>
            {
                try
                {
                    var runtime = await backend.ReadAsync(runtimeId, cancellationToken).ConfigureAwait(false);
                    return runtime is null ? Results.NotFound() : Results.Ok(runtime);
                }
                catch (RuntimeManagerException exception)
                {
                    return Failure(exception);
                }
            });
        group.MapPost(
            "/{runtimeId}/start",
            (string runtimeId, IRuntimeBackend backend, CancellationToken cancellationToken) =>
                ExecuteAsync(() => backend.StartAsync(runtimeId, cancellationToken)));
        group.MapPost(
            "/{runtimeId}/stop",
            (string runtimeId, IRuntimeBackend backend, CancellationToken cancellationToken) =>
                ExecuteAsync(() => backend.StopAsync(runtimeId, cancellationToken)));
        group.MapDelete(
            "/{runtimeId}",
            async (string runtimeId, IRuntimeBackend backend, CancellationToken cancellationToken) =>
            {
                try
                {
                    await backend.RemoveAsync(runtimeId, cancellationToken).ConfigureAwait(false);
                    return Results.NoContent();
                }
                catch (RuntimeManagerException exception)
                {
                    return Failure(exception);
                }
            });

        return endpoints;
    }

    private static async Task<IResult> ExecuteAsync(Func<Task<RuntimeResource>> operation)
    {
        try
        {
            return Results.Ok(await operation().ConfigureAwait(false));
        }
        catch (RuntimeManagerException exception)
        {
            return Failure(exception);
        }
    }

    private static IResult Failure(RuntimeManagerException exception)
    {
        var status = exception.Code switch
        {
            "runtime.not_found" => StatusCodes.Status404NotFound,
            "runtime.already_exists" => StatusCodes.Status409Conflict,
            "runtime.docker.unavailable" => StatusCodes.Status503ServiceUnavailable,
            _ when exception.Code.StartsWith("runtime.docker.", StringComparison.Ordinal) =>
                StatusCodes.Status502BadGateway,
            _ => StatusCodes.Status400BadRequest,
        };
        return Results.Json(new RuntimeError(exception.Code, exception.Message), statusCode: status);
    }
}
