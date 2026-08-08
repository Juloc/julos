using System.Text.Json;

namespace JulOS.PackageSdk;

/// <summary>One request sent over the private JulOS package-worker standard-I/O channel.</summary>
/// <param name="Id">Caller-generated request identifier.</param>
/// <param name="Method">Lifecycle method name.</param>
/// <param name="Payload">Method payload.</param>
/// <param name="DeadlineMilliseconds">Maximum processing time requested by the caller.</param>
public sealed record PackageWorkerProtocolRequest(
    string Id,
    string Method,
    JsonElement Payload,
    int DeadlineMilliseconds);

/// <summary>One response returned over the private JulOS package-worker standard-I/O channel.</summary>
/// <param name="Id">Matching request identifier.</param>
/// <param name="Succeeded">Whether the method completed successfully.</param>
/// <param name="Payload">Successful result payload.</param>
/// <param name="ErrorCode">Stable failure code.</param>
/// <param name="ErrorDetail">Caller-safe failure detail.</param>
public sealed record PackageWorkerProtocolResponse(
    string Id,
    bool Succeeded,
    JsonElement Payload,
    string? ErrorCode,
    string? ErrorDetail);

/// <summary>Hosts an <see cref="IJulOsPackageWorker"/> on a bounded line-delimited JSON channel.</summary>
public static class PackageWorkerHost
{
    /// <summary>Command-line switch required for the worker transport.</summary>
    public const string StandardIoSwitch = "--julos-worker-stdio";

    private const int MaximumLineCharacters = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Runs the worker host until shutdown, input closure or cancellation.</summary>
    /// <param name="worker">Package worker implementation.</param>
    /// <param name="args">Process arguments.</param>
    /// <param name="cancellationToken">Process shutdown token.</param>
    /// <returns>Zero for a controlled shutdown and two when the transport switch is absent.</returns>
    public static async Task<int> RunAsync(
        IJulOsPackageWorker worker,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(worker);
        ArgumentNullException.ThrowIfNull(args);
        if (!args.Contains(StandardIoSwitch, StringComparer.Ordinal))
        {
            Console.Error.WriteLine($"This package worker is hosted by JulOS. Start it with {StandardIoSwitch}.");
            return 2;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await Console.In.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                return 0;
            }
            if (line.Length is 0 or > MaximumLineCharacters)
            {
                await WriteAsync(Failure(string.Empty, "worker.request_invalid", "Worker request is invalid."))
                    .ConfigureAwait(false);
                continue;
            }

            PackageWorkerProtocolRequest request;
            try
            {
                request = JsonSerializer.Deserialize<PackageWorkerProtocolRequest>(line, JsonOptions)
                    ?? throw new JsonException("Request is empty.");
                ValidateRequest(request);
            }
            catch (JsonException)
            {
                await WriteAsync(Failure(string.Empty, "worker.request_invalid", "Worker request is invalid."))
                    .ConfigureAwait(false);
                continue;
            }

            var shutdown = string.Equals(request.Method, "shutdown", StringComparison.Ordinal);
            var response = await ExecuteAsync(worker, request, cancellationToken).ConfigureAwait(false);
            await WriteAsync(response).ConfigureAwait(false);
            if (shutdown && response.Succeeded)
            {
                return 0;
            }
        }

        return 0;
    }

    private static async Task<PackageWorkerProtocolResponse> ExecuteAsync(
        IJulOsPackageWorker worker,
        PackageWorkerProtocolRequest request,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMilliseconds(request.DeadlineMilliseconds));
        try
        {
            var result = request.Method switch
            {
                "validate" => JsonSerializer.SerializeToElement(
                    await worker.ValidateConfigurationAsync(
                        Deserialize<IReadOnlyDictionary<string, string>>(request.Payload),
                        deadline.Token).ConfigureAwait(false),
                    JsonOptions),
                "configure" => await ConfigureAsync(worker, request.Payload, deadline.Token).ConfigureAwait(false),
                "register" => JsonSerializer.SerializeToElement(
                    await worker.RegisterAsync(deadline.Token).ConfigureAwait(false),
                    JsonOptions),
                "start" => await RunAsync(worker.StartAsync, deadline.Token).ConfigureAwait(false),
                "stop" => await RunAsync(worker.StopAsync, deadline.Token).ConfigureAwait(false),
                "health" => JsonSerializer.SerializeToElement(
                    await worker.ReadHealthAsync(deadline.Token).ConfigureAwait(false),
                    JsonOptions),
                "command" => await InvokeCommandAsync(worker, request.Payload, deadline.Token).ConfigureAwait(false),
                "shutdown" => await RunAsync(worker.StopAsync, deadline.Token).ConfigureAwait(false),
                _ => throw new WorkerProtocolException("worker.method_unknown", "Worker method is not supported."),
            };
            return new PackageWorkerProtocolResponse(request.Id, true, result, null, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(request.Id, "worker.deadline_exceeded", "Worker operation exceeded its deadline.");
        }
        catch (WorkerProtocolException exception)
        {
            return Failure(request.Id, exception.Code, exception.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Console.Error.WriteLine($"Worker operation '{request.Method}' failed: {exception.GetType().Name}");
            return Failure(request.Id, "worker.operation_failed", "Worker operation failed.");
        }
    }

    private static async Task<JsonElement> ConfigureAsync(
        IJulOsPackageWorker worker,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        await worker.ConfigureAsync(Deserialize<PackageWorkerContext>(payload), cancellationToken)
            .ConfigureAwait(false);
        return EmptyPayload();
    }

    private static async Task<JsonElement> InvokeCommandAsync(
        IJulOsPackageWorker worker,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        if (worker is not IJulOsPackageCommandHandler commands)
        {
            throw new WorkerProtocolException(
                "worker.command_unsupported",
                "Package worker does not support private commands.");
        }

        return JsonSerializer.SerializeToElement(
            await commands.InvokeCommandAsync(
                Deserialize<PackageWorkerCommand>(payload),
                cancellationToken).ConfigureAwait(false),
            JsonOptions);
    }

    private static async Task<JsonElement> RunAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        await operation(cancellationToken).ConfigureAwait(false);
        return EmptyPayload();
    }

    private static T Deserialize<T>(JsonElement payload) =>
        payload.Deserialize<T>(JsonOptions)
        ?? throw new WorkerProtocolException("worker.payload_invalid", "Worker payload is invalid.");

    private static void ValidateRequest(PackageWorkerProtocolRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Id)
            || request.Id.Length > 64
            || string.IsNullOrWhiteSpace(request.Method)
            || request.Method.Length > 64
            || request.DeadlineMilliseconds is < 1 or > 300_000)
        {
            throw new JsonException("Request fields are invalid.");
        }
    }

    private static async Task WriteAsync(PackageWorkerProtocolResponse response)
    {
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(response, JsonOptions)).ConfigureAwait(false);
        await Console.Out.FlushAsync().ConfigureAwait(false);
    }

    private static PackageWorkerProtocolResponse Failure(string id, string code, string detail) =>
        new(id, false, EmptyPayload(), code, detail);

    private static JsonElement EmptyPayload() => JsonSerializer.SerializeToElement(new { }, JsonOptions);

    private sealed class WorkerProtocolException : Exception
    {
        internal WorkerProtocolException(string code, string message)
            : base(message)
        {
            this.Code = code;
        }

        internal string Code { get; }
    }
}
