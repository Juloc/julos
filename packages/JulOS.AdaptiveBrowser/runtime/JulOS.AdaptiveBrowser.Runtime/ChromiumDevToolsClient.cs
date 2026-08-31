using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;

namespace JulOS.AdaptiveBrowser.Runtime;

internal sealed class ChromiumDevToolsClient : IAsyncDisposable
{
    private const int MaximumCdpMessageBytes = 16 * 1024 * 1024;
    private readonly ClientWebSocket socket;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> pending = new();
    private readonly Task receiveLoop;
    private long nextCommandId;

    private ChromiumDevToolsClient(ClientWebSocket socket)
    {
        this.socket = socket;
        this.receiveLoop = Task.Run(() => this.ReceiveLoopAsync(this.lifetime.Token));
    }

    internal event Func<string, JsonElement, CancellationToken, Task>? EventReceived;

    internal static async Task<ChromiumDevToolsClient> ConnectAsync(CancellationToken cancellationToken)
    {
        using var http = new HttpClient
        {
            BaseAddress = new Uri("http://127.0.0.1:9222/"),
            Timeout = TimeSpan.FromSeconds(5),
        };
        JsonDocument targets;
        try
        {
            await using var stream = await http.GetStreamAsync("json/list", cancellationToken).ConfigureAwait(false);
            targets = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new InvalidOperationException("Chromium DevTools endpoint is unavailable.", exception);
        }

        using (targets)
        {
            var endpoint = targets.RootElement
                .EnumerateArray()
                .Where(item => item.TryGetProperty("type", out var type) && type.GetString() == "page")
                .Select(item => item.TryGetProperty("webSocketDebuggerUrl", out var url) ? url.GetString() : null)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                ?? throw new InvalidOperationException("Chromium has no debuggable page target.");
            var socket = new ClientWebSocket();
            try
            {
                await socket.ConnectAsync(new Uri(endpoint), cancellationToken).ConfigureAwait(false);
                return new ChromiumDevToolsClient(socket);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    }

    internal async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await this.CallAsync("Page.enable", null, cancellationToken).ConfigureAwait(false);
        await this.CallAsync("Runtime.enable", null, cancellationToken).ConfigureAwait(false);
        await this.CallAsync(
            "Page.startScreencast",
            new { format = "jpeg", quality = 82, everyNthFrame = 1 },
            cancellationToken).ConfigureAwait(false);
    }

    internal async Task<JsonElement> CallAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken)
    {
        if (this.socket.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("Chromium DevTools connection is closed.");
        }
        var id = Interlocked.Increment(ref this.nextCommandId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!this.pending.TryAdd(id, completion))
        {
            throw new InvalidOperationException("Chromium DevTools command identity collision.");
        }

        try
        {
            var payload = parameters is null
                ? JsonSerializer.SerializeToUtf8Bytes(new { id, method })
                : JsonSerializer.SerializeToUtf8Bytes(new { id, method, @params = parameters });
            await this.sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await this.socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                this.sendLock.Release();
            }
            return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            this.pending.TryRemove(id, out _);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await this.lifetime.CancelAsync().ConfigureAwait(false);
        try
        {
            if (this.socket.State == WebSocketState.Open)
            {
                await this.socket.CloseOutputAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "stream closed",
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (WebSocketException)
        {
        }
        try
        {
            await this.receiveLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        this.socket.Dispose();
        this.sendLock.Dispose();
        this.lifetime.Dispose();
        foreach (var completion in this.pending.Values)
        {
            completion.TrySetException(new InvalidOperationException("Chromium DevTools connection closed."));
        }
        this.pending.Clear();
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[32 * 1024];
        using var message = new MemoryStream();
        try
        {
            while (!cancellationToken.IsCancellationRequested && this.socket.State == WebSocketState.Open)
            {
                message.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await this.socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }
                    if (result.MessageType != WebSocketMessageType.Text)
                    {
                        throw new InvalidOperationException("Chromium DevTools returned a non-text message.");
                    }
                    if (message.Length + result.Count > MaximumCdpMessageBytes)
                    {
                        throw new InvalidOperationException("Chromium DevTools message exceeded the safety limit.");
                    }
                    await message.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken).ConfigureAwait(false);
                }
                while (!result.EndOfMessage);

                using var document = JsonDocument.Parse(
                    message.GetBuffer().AsMemory(0, checked((int)message.Length)));
                var root = document.RootElement;
                if (root.TryGetProperty("id", out var idElement) && idElement.TryGetInt64(out var id))
                {
                    if (!this.pending.TryGetValue(id, out var completion))
                    {
                        continue;
                    }
                    if (root.TryGetProperty("error", out var error))
                    {
                        var detail = error.TryGetProperty("message", out var errorMessage)
                            ? errorMessage.GetString() ?? "Chromium DevTools command failed."
                            : "Chromium DevTools command failed.";
                        completion.TrySetException(new InvalidOperationException(detail));
                    }
                    else
                    {
                        completion.TrySetResult(
                            root.TryGetProperty("result", out var response)
                                ? response.Clone()
                                : JsonSerializer.SerializeToElement(new { }));
                    }
                    continue;
                }

                if (root.TryGetProperty("method", out var methodElement)
                    && methodElement.ValueKind == JsonValueKind.String)
                {
                    var method = methodElement.GetString()!;
                    var parameters = root.TryGetProperty("params", out var parametersElement)
                        ? parametersElement.Clone()
                        : JsonSerializer.SerializeToElement(new { });
                    var handlers = this.EventReceived;
                    if (handlers is not null)
                    {
                        foreach (Func<string, JsonElement, CancellationToken, Task> handler in handlers.GetInvocationList())
                        {
                            _ = Task.Run(
                                async () =>
                                {
                                    try
                                    {
                                        await handler(method, parameters, this.lifetime.Token).ConfigureAwait(false);
                                    }
                                    catch (OperationCanceledException) when (this.lifetime.IsCancellationRequested)
                                    {
                                    }
                                },
                                CancellationToken.None);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            foreach (var completion in this.pending.Values)
            {
                completion.TrySetException(exception);
            }
        }
    }
}
