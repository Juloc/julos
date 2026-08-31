using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace JulOS.AdaptiveBrowser.Runtime;

internal sealed class BrowserStreamSession
{
    private const int MaximumClientMessageBytes = 64 * 1024;
    private readonly WebSocket browser;
    private readonly ILogger logger;
    private readonly SemaphoreSlim browserSendLock = new(1, 1);
    private ChromiumDevToolsClient? cdp;

    internal BrowserStreamSession(WebSocket browser, ILogger logger)
    {
        this.browser = browser ?? throw new ArgumentNullException(nameof(browser));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            this.cdp = await ChromiumDevToolsClient.ConnectAsync(cancellationToken).ConfigureAwait(false);
            this.cdp.EventReceived += this.OnCdpEventAsync;
            await this.cdp.InitializeAsync(cancellationToken).ConfigureAwait(false);
            await this.SendStateAsync(cancellationToken).ConfigureAwait(false);
            await this.ReceiveCommandsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (WebSocketException exception)
        {
            this.logger.LogInformation(exception, "Adaptive Browser stream closed.");
        }
        catch (Exception exception)
        {
            this.logger.LogWarning(exception, "Adaptive Browser stream failed.");
            await this.TrySendErrorAsync(
                "adaptive-browser.stream_failed",
                "The server browser stream failed.",
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (this.cdp is not null)
            {
                this.cdp.EventReceived -= this.OnCdpEventAsync;
                await this.cdp.DisposeAsync().ConfigureAwait(false);
            }
            this.browserSendLock.Dispose();
        }
    }

    private async Task ReceiveCommandsAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        while (!cancellationToken.IsCancellationRequested && this.browser.State == WebSocketState.Open)
        {
            message.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                result = await this.browser.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }
                if (result.MessageType != WebSocketMessageType.Text)
                {
                    await this.TrySendErrorAsync(
                        "adaptive-browser.message_invalid",
                        "Adaptive Browser accepts JSON control messages only.",
                        cancellationToken).ConfigureAwait(false);
                    break;
                }
                if (message.Length + result.Count > MaximumClientMessageBytes)
                {
                    await this.TrySendErrorAsync(
                        "adaptive-browser.message_too_large",
                        "Adaptive Browser control message is too large.",
                        cancellationToken).ConfigureAwait(false);
                    return;
                }
                await message.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken).ConfigureAwait(false);
            }
            while (!result.EndOfMessage);

            if (message.Length == 0)
            {
                continue;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(message.GetBuffer().AsSpan(0, checked((int)message.Length)));
            }
            catch (JsonException)
            {
                await this.TrySendErrorAsync(
                    "adaptive-browser.message_invalid",
                    "Adaptive Browser control message is invalid JSON.",
                    cancellationToken).ConfigureAwait(false);
                continue;
            }

            using (document)
            {
                await this.HandleCommandAsync(document.RootElement, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleCommandAsync(JsonElement command, CancellationToken cancellationToken)
    {
        if (this.cdp is null
            || command.ValueKind != JsonValueKind.Object
            || !command.TryGetProperty("type", out var typeElement)
            || typeElement.ValueKind != JsonValueKind.String)
        {
            await this.TrySendErrorAsync(
                "adaptive-browser.message_invalid",
                "Adaptive Browser control message is invalid.",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var type = typeElement.GetString();
        try
        {
            switch (type)
            {
                case "navigate":
                    var rawUrl = ReadString(command, "url", 4096);
                    if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var url)
                        || url.Scheme is not ("http" or "https")
                        || !string.IsNullOrEmpty(url.UserInfo))
                    {
                        throw new BrowserCommandException("The browser URL must use HTTP or HTTPS.");
                    }
                    await this.cdp.CallAsync("Page.navigate", new { url = url.AbsoluteUri }, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case "resize":
                    var width = Math.Clamp(ReadInt(command, "width"), 320, 3840);
                    var height = Math.Clamp(ReadInt(command, "height"), 240, 2160);
                    var scale = Math.Clamp(ReadDouble(command, "deviceScaleFactor"), 0.5d, 3d);
                    await this.cdp.CallAsync(
                        "Emulation.setDeviceMetricsOverride",
                        new { width, height, deviceScaleFactor = scale, mobile = false },
                        cancellationToken).ConfigureAwait(false);
                    break;
                case "pointer":
                    await this.DispatchPointerAsync(command, cancellationToken).ConfigureAwait(false);
                    break;
                case "wheel":
                    await this.cdp.CallAsync(
                        "Input.dispatchMouseEvent",
                        new
                        {
                            type = "mouseWheel",
                            x = ReadDouble(command, "x"),
                            y = ReadDouble(command, "y"),
                            deltaX = ReadDouble(command, "deltaX"),
                            deltaY = ReadDouble(command, "deltaY"),
                        },
                        cancellationToken).ConfigureAwait(false);
                    break;
                case "key":
                    await this.DispatchKeyAsync(command, cancellationToken).ConfigureAwait(false);
                    break;
                case "back":
                    await this.NavigateHistoryAsync(-1, cancellationToken).ConfigureAwait(false);
                    break;
                case "forward":
                    await this.NavigateHistoryAsync(1, cancellationToken).ConfigureAwait(false);
                    break;
                case "reload":
                    await this.cdp.CallAsync("Page.reload", new { ignoreCache = false }, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case "stop":
                    await this.cdp.CallAsync("Page.stopLoading", null, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    throw new BrowserCommandException("Adaptive Browser command is not supported.");
            }
        }
        catch (BrowserCommandException exception)
        {
            await this.TrySendErrorAsync(
                "adaptive-browser.command_invalid",
                exception.Message,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task DispatchPointerAsync(JsonElement command, CancellationToken cancellationToken)
    {
        if (this.cdp is null)
        {
            return;
        }
        var kind = ReadString(command, "kind", 16);
        var button = ReadString(command, "button", 16);
        var type = kind switch
        {
            "move" => "mouseMoved",
            "down" => "mousePressed",
            "up" => "mouseReleased",
            _ => throw new BrowserCommandException("Pointer event kind is invalid."),
        };
        if (button is not ("left" or "middle" or "right"))
        {
            throw new BrowserCommandException("Pointer button is invalid.");
        }
        await this.cdp.CallAsync(
            "Input.dispatchMouseEvent",
            new
            {
                type,
                x = ReadDouble(command, "x"),
                y = ReadDouble(command, "y"),
                button = kind == "move" ? "none" : button,
                buttons = command.TryGetProperty("buttons", out var buttonsElement) && buttonsElement.TryGetInt32(out var buttons)
                    ? Math.Clamp(buttons, 0, 31)
                    : 0,
                clickCount = kind == "move" ? 0 : 1,
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task DispatchKeyAsync(JsonElement command, CancellationToken cancellationToken)
    {
        if (this.cdp is null)
        {
            return;
        }
        var kind = ReadString(command, "kind", 16);
        var key = ReadString(command, "key", 128);
        var code = ReadString(command, "code", 128);
        var text = command.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String
            ? textElement.GetString() ?? string.Empty
            : string.Empty;
        if (text.Length > 8)
        {
            throw new BrowserCommandException("Keyboard text is invalid.");
        }
        var modifiers = command.TryGetProperty("modifiers", out var modifiersElement) && modifiersElement.TryGetInt32(out var parsedModifiers)
            ? Math.Clamp(parsedModifiers, 0, 15)
            : 0;
        var type = kind switch
        {
            "down" => text.Length > 0 ? "keyDown" : "rawKeyDown",
            "up" => "keyUp",
            _ => throw new BrowserCommandException("Keyboard event kind is invalid."),
        };
        await this.cdp.CallAsync(
            "Input.dispatchKeyEvent",
            new { type, key, code, text = kind == "down" ? text : string.Empty, modifiers },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task NavigateHistoryAsync(int offset, CancellationToken cancellationToken)
    {
        if (this.cdp is null)
        {
            return;
        }
        var history = await this.cdp.CallAsync("Page.getNavigationHistory", null, cancellationToken)
            .ConfigureAwait(false);
        var currentIndex = history.GetProperty("currentIndex").GetInt32();
        var entries = history.GetProperty("entries").EnumerateArray().ToArray();
        var targetIndex = currentIndex + offset;
        if (targetIndex < 0 || targetIndex >= entries.Length)
        {
            return;
        }
        var entryId = entries[targetIndex].GetProperty("id").GetInt32();
        await this.cdp.CallAsync("Page.navigateToHistoryEntry", new { entryId }, cancellationToken)
            .ConfigureAwait(false);
    }

    private Task OnCdpEventAsync(string method, JsonElement parameters, CancellationToken cancellationToken)
    {
        return method switch
        {
            "Page.screencastFrame" => this.SendFrameAsync(parameters, cancellationToken),
            "Page.loadEventFired" => this.SendStateAsync(cancellationToken),
            _ => Task.CompletedTask,
        };
    }

    private async Task SendFrameAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        if (this.cdp is null
            || !parameters.TryGetProperty("data", out var dataElement)
            || dataElement.ValueKind != JsonValueKind.String
            || !parameters.TryGetProperty("sessionId", out var sessionElement)
            || !sessionElement.TryGetInt32(out var sessionId))
        {
            return;
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(dataElement.GetString() ?? string.Empty);
        }
        catch (FormatException)
        {
            return;
        }
        try
        {
            await this.SendBinaryAsync(bytes, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await this.cdp.CallAsync(
                "Page.screencastFrameAck",
                new { sessionId },
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SendStateAsync(CancellationToken cancellationToken)
    {
        if (this.cdp is null)
        {
            return;
        }
        try
        {
            var history = await this.cdp.CallAsync("Page.getNavigationHistory", null, cancellationToken)
                .ConfigureAwait(false);
            var currentIndex = history.GetProperty("currentIndex").GetInt32();
            var entries = history.GetProperty("entries").EnumerateArray().ToArray();
            if (entries.Length == 0 || currentIndex < 0 || currentIndex >= entries.Length)
            {
                return;
            }
            var current = entries[currentIndex];
            await this.SendJsonAsync(
                new
                {
                    type = "state",
                    url = current.GetProperty("url").GetString() ?? string.Empty,
                    title = current.TryGetProperty("title", out var titleElement)
                        ? titleElement.GetString() ?? string.Empty
                        : string.Empty,
                    canGoBack = currentIndex > 0,
                    canGoForward = currentIndex < entries.Length - 1,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            this.logger.LogDebug(exception, "Adaptive Browser state could not be read from Chromium.");
        }
    }

    private async Task SendJsonAsync(object payload, CancellationToken cancellationToken)
    {
        if (this.browser.State != WebSocketState.Open)
        {
            return;
        }
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        await this.browserSendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await this.browser.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            this.browserSendLock.Release();
        }
    }

    private async Task SendBinaryAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        if (this.browser.State != WebSocketState.Open)
        {
            return;
        }
        await this.browserSendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await this.browser.SendAsync(bytes, WebSocketMessageType.Binary, true, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            this.browserSendLock.Release();
        }
    }

    private async Task TrySendErrorAsync(string code, string detail, CancellationToken cancellationToken)
    {
        try
        {
            await this.SendJsonAsync(new { type = "error", code, detail }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException)
        {
        }
    }

    private static string ReadString(JsonElement source, string name, int maximumLength)
    {
        if (!source.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString())
            || value.GetString()!.Length > maximumLength)
        {
            throw new BrowserCommandException($"{name} is invalid.");
        }
        return value.GetString()!;
    }

    private static int ReadInt(JsonElement source, string name)
    {
        if (!source.TryGetProperty(name, out var value) || !value.TryGetInt32(out var result))
        {
            throw new BrowserCommandException($"{name} is invalid.");
        }
        return result;
    }

    private static double ReadDouble(JsonElement source, string name)
    {
        if (!source.TryGetProperty(name, out var value) || !value.TryGetDouble(out var result) || !double.IsFinite(result))
        {
            throw new BrowserCommandException($"{name} is invalid.");
        }
        return result;
    }

    private sealed class BrowserCommandException : Exception
    {
        internal BrowserCommandException(string message)
            : base(message)
        {
        }
    }
}
