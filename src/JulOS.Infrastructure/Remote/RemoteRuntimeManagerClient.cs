using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using JulOS.Application.Remote;
using JulOS.Contracts.Runtime;

using Microsoft.Extensions.Configuration;

namespace JulOS.Infrastructure.Remote;

internal sealed record RemoteRuntimeManagerClientOptions(Uri Endpoint, string ApiKey)
{
    internal static RemoteRuntimeManagerClientOptions? Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var endpointValue = configuration["Remote:RuntimeManager:Endpoint"]
            ?? Environment.GetEnvironmentVariable("JULOS_REMOTE_RUNTIME_MANAGER_ENDPOINT");
        var apiKey = configuration["Remote:RuntimeManager:ApiKey"]
            ?? Environment.GetEnvironmentVariable("JULOS_RUNTIME_MANAGER_KEY");

        if (string.IsNullOrWhiteSpace(endpointValue) && string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }
        if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException(
                "Remote:RuntimeManager:Endpoint must be an absolute HTTP or HTTPS URI.");
        }
        if (apiKey is null || apiKey.Length < 32 || apiKey.Any(char.IsControl))
        {
            throw new InvalidOperationException(
                "Remote Runtime Manager API key must contain at least 32 non-control characters.");
        }

        return new RemoteRuntimeManagerClientOptions(endpoint, apiKey);
    }
}

/// <summary>Authenticated HTTP adapter for the narrow Runtime Manager API.</summary>
internal sealed class HttpRemoteRuntimeManager : IRemoteRuntimeManager, IDisposable
{
    private const string ApiKeyHeader = "X-JulOS-Runtime-Key";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private readonly HttpClient client;
    private readonly string apiKey;

    internal HttpRemoteRuntimeManager(RemoteRuntimeManagerClientOptions options)
        : this(new HttpClient { BaseAddress = EnsureTrailingSlash(options.Endpoint) }, options.ApiKey)
    {
    }

    internal HttpRemoteRuntimeManager(HttpClient client, string apiKey)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        if (client.BaseAddress is null)
        {
            throw new ArgumentException("Runtime Manager client requires a base address.", nameof(client));
        }
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("Runtime Manager API key is required.", nameof(apiKey));
        }
        this.apiKey = apiKey;
    }

    /// <inheritdoc />
    public async Task<PackageRuntimeResponse> AllocateAndStartAsync(
        CreatePackageRuntimeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = await this.CreateOrRecoverAsync(request, cancellationToken).ConfigureAwait(false);
        VerifyIdentity(created, request);

        using var start = Request(HttpMethod.Post, $"v1/runtimes/{request.InstanceId}/start");
        using var response = await this.SendAsync(start, cancellationToken).ConfigureAwait(false);
        var started = await ReadSuccessAsync<PackageRuntimeResponse>(response, cancellationToken)
            .ConfigureAwait(false);
        VerifyIdentity(started, request);
        return started;
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string runtimeId, CancellationToken cancellationToken = default)
    {
        ValidateRuntimeId(runtimeId);
        using var request = Request(HttpMethod.Delete, $"v1/runtimes/{runtimeId}");
        using var response = await this.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound)
        {
            return;
        }
        throw await ReadFailureAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Releases the owned HTTP client.</summary>
    public void Dispose()
    {
        this.client.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<PackageRuntimeResponse> CreateOrRecoverAsync(
        CreatePackageRuntimeRequest request,
        CancellationToken cancellationToken)
    {
        using var create = Request(HttpMethod.Post, "v1/runtimes/");
        create.Content = JsonContent.Create(request, options: JsonOptions);
        using var response = await this.SendAsync(create, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Conflict)
        {
            return await ReadSuccessAsync<PackageRuntimeResponse>(response, cancellationToken)
                .ConfigureAwait(false);
        }

        using var read = Request(HttpMethod.Get, $"v1/runtimes/{request.InstanceId}");
        using var existingResponse = await this.SendAsync(read, cancellationToken).ConfigureAwait(false);
        return await ReadSuccessAsync<PackageRuntimeResponse>(existingResponse, cancellationToken)
            .ConfigureAwait(false);
    }

    private HttpRequestMessage Request(HttpMethod method, string relativePath)
    {
        var request = new HttpRequestMessage(method, relativePath);
        request.Headers.TryAddWithoutValidation(ApiKeyHeader, this.apiKey);
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(RequestTimeout);
        try
        {
            return await this.client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new RemoteRuntimeManagerException(
                "runtime.request_timeout",
                "Runtime Manager did not respond before the request deadline.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new RemoteRuntimeManagerException(
                "runtime.unavailable",
                "Runtime Manager is unavailable.",
                exception);
        }
    }

    private static async Task<T> ReadSuccessAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw await ReadFailureAsync(response, cancellationToken).ConfigureAwait(false);
        }
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new RemoteRuntimeManagerException(
                    "runtime.response_invalid",
                    "Runtime Manager returned an invalid response.");
        }
        catch (JsonException exception)
        {
            throw new RemoteRuntimeManagerException(
                "runtime.response_invalid",
                "Runtime Manager returned an invalid response.",
                exception);
        }
    }

    private static async Task<RemoteRuntimeManagerException> ReadFailureAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var failure = await response.Content
                .ReadFromJsonAsync<RuntimeManagerErrorResponse>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            if (failure is not null
                && !string.IsNullOrWhiteSpace(failure.Code)
                && !string.IsNullOrWhiteSpace(failure.Message))
            {
                return new RemoteRuntimeManagerException(failure.Code, failure.Message);
            }
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return new RemoteRuntimeManagerException(
                "runtime.response_invalid",
                "Runtime Manager returned an invalid failure response.",
                exception);
        }

        return new RemoteRuntimeManagerException(
            "runtime.request_failed",
            "Runtime Manager rejected the request.");
    }

    private static void VerifyIdentity(
        PackageRuntimeResponse runtime,
        CreatePackageRuntimeRequest request)
    {
        if (!string.Equals(runtime.RuntimeId, request.InstanceId, StringComparison.Ordinal)
            || !string.Equals(runtime.PackageId, request.PackageId, StringComparison.Ordinal)
            || !string.Equals(runtime.PackageVersion, request.PackageVersion, StringComparison.Ordinal)
            || !string.Equals(runtime.InstanceId, request.InstanceId, StringComparison.Ordinal)
            || !string.Equals(runtime.Image, request.Image, StringComparison.Ordinal))
        {
            throw new RemoteRuntimeManagerException(
                "runtime.identity_mismatch",
                "Runtime Manager returned a runtime owned by a different package or instance.");
        }
    }

    private static void ValidateRuntimeId(string runtimeId)
    {
        if (string.IsNullOrWhiteSpace(runtimeId)
            || runtimeId.Length > 64
            || runtimeId.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '-')))
        {
            throw new ArgumentException("Runtime identity is invalid.", nameof(runtimeId));
        }
    }

    private static Uri EnsureTrailingSlash(Uri endpoint) =>
        endpoint.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? endpoint
            : new Uri(endpoint.AbsoluteUri + '/', UriKind.Absolute);
}

/// <summary>Explicit refusal used when no Runtime Manager endpoint is configured.</summary>
internal sealed class UnavailableRemoteRuntimeManager : IRemoteRuntimeManager
{
    /// <inheritdoc />
    public Task<PackageRuntimeResponse> AllocateAndStartAsync(
        CreatePackageRuntimeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        throw new RemoteRuntimeManagerException(
            "runtime.not_configured",
            "Runtime Manager is not configured for Remote sessions.");
    }

    /// <inheritdoc />
    public Task RemoveAsync(string runtimeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
