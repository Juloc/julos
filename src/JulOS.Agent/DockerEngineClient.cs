using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace JulOS.Agent;

internal sealed record DockerInventoryReadRequest(string Kind, int Page = 0, int PageSize = 50);
internal sealed record DockerLogsReadRequest(string ContainerId, int Tail = 200);
internal sealed record DockerControlRequest(string ContainerId, string Action);

internal sealed class DockerCommandException : Exception
{
    internal DockerCommandException(string code, string message, Exception? inner = null)
        : base(message, inner) => this.Code = code;

    internal string Code { get; }
}

/// <summary>Bounded local Docker Engine adapter; arbitrary Engine requests are never exposed.</summary>
internal sealed class DockerEngineClient : IDisposable
{
    private const int MaximumPageSize = 100;
    private const int MaximumLogBytes = 48 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly DockerEngineOptions options;
    private readonly HttpClient client;
    private readonly bool ownsClient;

    internal DockerEngineClient(DockerEngineOptions options)
        : this(options, CreateClient(options), true)
    {
    }

    internal DockerEngineClient(DockerEngineOptions options, HttpClient client)
        : this(options, client, false)
    {
    }

    private DockerEngineClient(DockerEngineOptions options, HttpClient client, bool ownsClient)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        if (!options.Enabled)
        {
            throw new InvalidOperationException("Docker Engine access is not enabled.");
        }
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.ownsClient = ownsClient;
    }

    internal async Task<JsonElement> ReadInventoryAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var request = Deserialize<DockerInventoryReadRequest>(payload, "Docker inventory request is invalid.");
        if (request.Page < 0 || request.PageSize is < 1 or > MaximumPageSize)
        {
            throw Failure("docker.request_invalid", "Docker inventory page is invalid.");
        }

        return request.Kind switch
        {
            "engine" => await ReadEngineAsync(request, cancellationToken).ConfigureAwait(false),
            "containers" => await ReadContainersAsync(request, cancellationToken).ConfigureAwait(false),
            "images" => await ReadArrayAsync(request, "/images/json?all=1", Image, cancellationToken).ConfigureAwait(false),
            "networks" => await ReadArrayAsync(request, "/networks", Network, cancellationToken).ConfigureAwait(false),
            "volumes" => await ReadVolumesAsync(request, cancellationToken).ConfigureAwait(false),
            _ => throw Failure("docker.inventory_kind_unsupported", "Docker inventory kind is not supported."),
        };
    }

    internal async Task<JsonElement> ReadLogsAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var request = Deserialize<DockerLogsReadRequest>(payload, "Docker logs request is invalid.");
        ValidateContainerId(request.ContainerId);
        if (request.Tail is < 1 or > 500)
        {
            throw Failure("docker.request_invalid", "Docker log tail is invalid.");
        }

        using var response = await SendAsync(
            HttpMethod.Get,
            $"/containers/{Uri.EscapeDataString(request.ContainerId)}/logs?stdout=1&stderr=1&timestamps=1&tail={request.Tail}",
            cancellationToken).ConfigureAwait(false);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var text = DecodeLogs(bytes);
        var encoded = Encoding.UTF8.GetBytes(text);
        if (encoded.Length > MaximumLogBytes)
        {
            text = Encoding.UTF8.GetString(encoded.AsSpan(encoded.Length - MaximumLogBytes));
        }
        return JsonSerializer.SerializeToElement(new { text }, JsonOptions);
    }

    internal async Task<JsonElement> ControlAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        if (!this.options.ControlEnabled)
        {
            throw Failure("docker.control_disabled", "Docker control is disabled on this Agent.");
        }

        var request = Deserialize<DockerControlRequest>(payload, "Docker control request is invalid.");
        ValidateContainerId(request.ContainerId);
        var path = request.Action switch
        {
            "start" => $"/containers/{Uri.EscapeDataString(request.ContainerId)}/start",
            "stop" => $"/containers/{Uri.EscapeDataString(request.ContainerId)}/stop?t=10",
            "restart" => $"/containers/{Uri.EscapeDataString(request.ContainerId)}/restart?t=10",
            _ => throw Failure("docker.control_action_unsupported", "Docker control action is not supported."),
        };
        using var response = await SendAsync(HttpMethod.Post, path, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.SerializeToElement(new { request.ContainerId, request.Action, succeeded = true }, JsonOptions);
    }

    public void Dispose()
    {
        if (this.ownsClient)
        {
            this.client.Dispose();
        }
    }

    private async Task<JsonElement> ReadEngineAsync(DockerInventoryReadRequest request, CancellationToken cancellationToken)
    {
        if (request.Page > 0)
        {
            return Page(request, [], 1);
        }
        using var version = await GetJsonAsync("/version", cancellationToken).ConfigureAwait(false);
        using var info = await GetJsonAsync("/info", cancellationToken).ConfigureAwait(false);
        return Page(request, new object[]
        {
            new
            {
                id = Text(info.RootElement, "ID"),
                name = Text(info.RootElement, "Name"),
                serverVersion = Text(version.RootElement, "Version"),
                apiVersion = Text(version.RootElement, "ApiVersion"),
                operatingSystem = Text(info.RootElement, "OperatingSystem"),
                architecture = Text(info.RootElement, "Architecture"),
                cpus = Number(info.RootElement, "NCPU"),
                memoryBytes = Number(info.RootElement, "MemTotal"),
                containers = Number(info.RootElement, "Containers"),
                images = Number(info.RootElement, "Images"),
                storageDriver = Text(info.RootElement, "Driver"),
            },
        }, 1);
    }

    private async Task<JsonElement> ReadContainersAsync(DockerInventoryReadRequest request, CancellationToken cancellationToken)
    {
        using var list = await GetJsonAsync("/containers/json?all=1", cancellationToken).ConfigureAwait(false);
        EnsureArray(list.RootElement, "Docker container inventory response is invalid.");
        var all = list.RootElement.EnumerateArray().ToArray();
        var selected = Slice(all, request);
        var items = new List<object>(selected.Length);
        foreach (var container in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = Text(container, "Id") ?? string.Empty;
            JsonDocument? inspect = null;
            try
            {
                if (id.Length > 0)
                {
                    inspect = await GetJsonAsync($"/containers/{Uri.EscapeDataString(id)}/json", cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (DockerCommandException exception) when (exception.Code == "docker.resource_not_found")
            {
            }

            using (inspect)
            {
                JsonElement state = default;
                if (inspect is not null && inspect.RootElement.TryGetProperty("State", out var foundState))
                {
                    state = foundState;
                }
                items.Add(new
                {
                    runtimeId = id,
                    names = Strings(container, "Names", 8),
                    image = Text(container, "Image"),
                    imageId = Text(container, "ImageID"),
                    state = Text(container, "State"),
                    status = Text(container, "Status"),
                    created = Number(container, "Created"),
                    labels = Labels(container),
                    ports = Ports(container),
                    mounts = Mounts(container),
                    restartCount = inspect is null ? 0 : Number(inspect.RootElement, "RestartCount"),
                    health = state.ValueKind == JsonValueKind.Object && state.TryGetProperty("Health", out var health)
                        ? Text(health, "Status")
                        : null,
                    oomKilled = state.ValueKind == JsonValueKind.Object && Flag(state, "OOMKilled"),
                    error = state.ValueKind == JsonValueKind.Object ? Text(state, "Error") : null,
                    exitCode = state.ValueKind == JsonValueKind.Object ? Number(state, "ExitCode") : 0,
                });
            }
        }
        return Page(request, items, all.Length);
    }

    private async Task<JsonElement> ReadVolumesAsync(DockerInventoryReadRequest request, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync("/volumes", cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("Volumes", out var volumes)
            || volumes.ValueKind is not (JsonValueKind.Array or JsonValueKind.Null))
        {
            throw Failure("docker.engine_response_invalid", "Docker volume inventory response is invalid.");
        }
        var all = volumes.ValueKind == JsonValueKind.Array ? volumes.EnumerateArray().ToArray() : [];
        var items = Slice(all, request).Select(volume => (object)new
        {
            name = Text(volume, "Name"),
            driver = Text(volume, "Driver"),
            scope = Text(volume, "Scope"),
            labels = Labels(volume),
        }).ToArray();
        return Page(request, items, all.Length);
    }

    private async Task<JsonElement> ReadArrayAsync(
        DockerInventoryReadRequest request,
        string path,
        Func<JsonElement, object> map,
        CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(path, cancellationToken).ConfigureAwait(false);
        EnsureArray(document.RootElement, "Docker inventory response is invalid.");
        var all = document.RootElement.EnumerateArray().ToArray();
        return Page(request, Slice(all, request).Select(map).ToArray(), all.Length);
    }

    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, path, cancellationToken).ConfigureAwait(false);
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw Failure("docker.engine_response_invalid", "Docker Engine returned invalid JSON.", exception);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(method, path);
            var response = await this.client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotModified)
            {
                return response;
            }
            var status = response.StatusCode;
            response.Dispose();
            throw status switch
            {
                HttpStatusCode.NotFound => Failure("docker.resource_not_found", "Docker resource does not exist."),
                HttpStatusCode.Conflict => Failure("docker.control_conflict", "Docker Engine rejected the state transition."),
                HttpStatusCode.Forbidden => Failure("docker.engine_permission_denied", "Docker Engine access was denied."),
                _ => Failure("docker.engine_request_failed", "Docker Engine request failed."),
            };
        }
        catch (DockerCommandException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or SocketException or IOException)
        {
            throw Failure("docker.engine_unreachable", "Docker Engine is unreachable.", exception);
        }
    }

    private static HttpClient CreateClient(DockerEngineOptions options)
    {
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, cancellationToken) =>
            {
                if (!OperatingSystem.IsLinux())
                {
                    throw new PlatformNotSupportedException("Docker Unix socket access requires a Linux Agent.");
                }
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try
                {
                    await socket.ConnectAsync(new UnixDomainSocketEndPoint(options.SocketPath), cancellationToken)
                        .ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };
        return new HttpClient(handler, true)
        {
            BaseAddress = new Uri("http://localhost", UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(15),
        };
    }

    private static T Deserialize<T>(JsonElement payload, string message)
    {
        try
        {
            return payload.Deserialize<T>(JsonOptions) ?? throw new JsonException();
        }
        catch (JsonException exception)
        {
            throw Failure("docker.request_invalid", message, exception);
        }
    }

    private static object Image(JsonElement image) => new
    {
        runtimeId = Text(image, "Id"),
        repoTags = Strings(image, "RepoTags", 32),
        repoDigests = Strings(image, "RepoDigests", 32),
        created = Number(image, "Created"),
        sizeBytes = Number(image, "Size"),
        labels = Labels(image),
    };

    private static object Network(JsonElement network) => new
    {
        runtimeId = Text(network, "Id"),
        name = Text(network, "Name"),
        driver = Text(network, "Driver"),
        scope = Text(network, "Scope"),
        internalNetwork = Flag(network, "Internal"),
        attachable = Flag(network, "Attachable"),
        labels = Labels(network),
    };

    private static object[] Ports(JsonElement container) =>
        !container.TryGetProperty("Ports", out var values) || values.ValueKind != JsonValueKind.Array
            ? []
            : values.EnumerateArray().Take(32).Select(port => (object)new
            {
                privatePort = Number(port, "PrivatePort"),
                publicPort = Number(port, "PublicPort"),
                type = Text(port, "Type"),
                ip = Text(port, "IP"),
            }).ToArray();

    private static object[] Mounts(JsonElement container) =>
        !container.TryGetProperty("Mounts", out var values) || values.ValueKind != JsonValueKind.Array
            ? []
            : values.EnumerateArray().Take(32).Select(mount => (object)new
            {
                type = Text(mount, "Type"),
                name = Text(mount, "Name"),
                destination = Text(mount, "Destination"),
                readWrite = Flag(mount, "RW"),
            }).ToArray();

    private static IReadOnlyDictionary<string, string> Labels(JsonElement element)
    {
        if (!element.TryGetProperty("Labels", out var labels) || labels.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
        return labels.EnumerateObject().Where(item => item.Value.ValueKind == JsonValueKind.String).Take(64)
            .ToDictionary(
                item => Limit(item.Name, 128) ?? string.Empty,
                item => Limit(item.Value.GetString(), 512) ?? string.Empty,
                StringComparer.Ordinal);
    }

    private static string[] Strings(JsonElement element, string property, int maximum) =>
        !element.TryGetProperty(property, out var values) || values.ValueKind != JsonValueKind.Array
            ? []
            : values.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => Limit(value.GetString(), 1024)).Where(value => value is not null)
                .Take(maximum).Cast<string>().ToArray();

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? Limit(value.GetString(), 1024)
            : null;

    private static long Number(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.TryGetInt64(out var number)
            ? number
            : 0;

    private static bool Flag(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
        && value.GetBoolean();

    private static JsonElement Page<T>(DockerInventoryReadRequest request, IReadOnlyCollection<T> items, int total) =>
        JsonSerializer.SerializeToElement(new { kind = request.Kind, request.Page, request.PageSize, total, items }, JsonOptions);

    private static JsonElement[] Slice(JsonElement[] all, DockerInventoryReadRequest request)
    {
        var offset = (long)request.Page * request.PageSize;
        return offset >= all.Length ? [] : all.Skip((int)offset).Take(request.PageSize).ToArray();
    }

    private static void EnsureArray(JsonElement value, string message)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw Failure("docker.engine_response_invalid", message);
        }
    }

    private static void ValidateContainerId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length is < 12 or > 64 || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw Failure("docker.container_identity_invalid", "Docker container identity is invalid.");
        }
    }

    private static string DecodeLogs(byte[] bytes)
    {
        if (bytes.Length < 8 || bytes[0] is not (1 or 2))
        {
            return Encoding.UTF8.GetString(bytes);
        }
        using var output = new MemoryStream();
        var offset = 0;
        while (offset + 8 <= bytes.Length)
        {
            var length = (bytes[offset + 4] << 24) | (bytes[offset + 5] << 16) | (bytes[offset + 6] << 8) | bytes[offset + 7];
            offset += 8;
            if (length < 0 || offset + length > bytes.Length)
            {
                return Encoding.UTF8.GetString(bytes);
            }
            output.Write(bytes, offset, length);
            offset += length;
        }
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static string? Limit(string? value, int maximum) =>
        value is null ? null : value.Length <= maximum ? value : value[..maximum];

    private static DockerCommandException Failure(string code, string message, Exception? inner = null) => new(code, message, inner);
}
