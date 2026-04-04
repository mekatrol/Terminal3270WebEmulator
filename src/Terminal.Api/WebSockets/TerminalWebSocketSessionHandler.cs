using Microsoft.Extensions.Options;
using System.Buffers.Binary;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Terminal.Api.Options;
using Terminal.Common.Models;
using Terminal.Common.Options;
using Terminal.Common.Services;

namespace Terminal.Api.WebSockets;

internal sealed class TerminalWebSocketSessionHandler
{
    private static readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IOptions<TerminalProxyOptions> _terminalProxyOptions;
    private readonly IOptions<Tn3270EOptions> _tn3270EOptions;
    private readonly ILogger<TerminalWebSocketSessionHandler> _logger;

    public TerminalWebSocketSessionHandler(
        IServiceScopeFactory serviceScopeFactory,
        IOptions<TerminalProxyOptions> terminalProxyOptions,
        IOptions<Tn3270EOptions> tn3270EOptions,
        ILogger<TerminalWebSocketSessionHandler> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _terminalProxyOptions = terminalProxyOptions;
        _tn3270EOptions = tn3270EOptions;
        _logger = logger;
    }

    public async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("A WebSocket upgrade request is required.");
            return;
        }

        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        await using var scope = _serviceScopeFactory.CreateAsyncScope();

        var proxyOptions = _terminalProxyOptions.Value;
        var hostOptions = _tn3270EOptions.Value;
        var terminalService = scope.ServiceProvider.GetRequiredService<ITn3270EService>();

        using var sessionCancellationSource =
            CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        sessionCancellationSource.CancelAfter(proxyOptions.SessionLifetime);
        var closeDescription = "Terminal proxy session closed.";

        try
        {
            await terminalService.ConnectAsync(
                hostOptions.Host,
                hostOptions.Port,
                sessionCancellationSource.Token);

            var negotiated = await terminalService.NegotiateAsync(
                hostOptions.TerminalType,
                hostOptions.DeviceName,
                sessionCancellationSource.Token);

            if (!negotiated)
            {
                await SendControlMessageAsync(
                    webSocket,
                    new TerminalSessionErrorMessage(
                        "session-error",
                        "The configured TN3270/TN3270E host rejected terminal negotiation."),
                    sessionCancellationSource.Token);

                await CloseWebSocketIfOpenAsync(
                    webSocket,
                    WebSocketCloseStatus.PolicyViolation,
                    "Terminal negotiation failed.",
                    CancellationToken.None);
                return;
            }

            await SendControlMessageAsync(
                webSocket,
                new TerminalSessionReadyMessage(
                    "session-ready",
                    hostOptions.Host,
                    hostOptions.Port,
                    hostOptions.TerminalType,
                    hostOptions.DeviceName,
                    proxyOptions.SessionLifetime),
                sessionCancellationSource.Token);

            var browserToHostTask = ProxyBrowserToHostAsync(
                webSocket,
                terminalService,
                sessionCancellationSource.Token);
            var hostToBrowserTask = ProxyHostToBrowserAsync(
                webSocket,
                terminalService,
                sessionCancellationSource.Token);

            _ = await Task.WhenAny(browserToHostTask, hostToBrowserTask);
            sessionCancellationSource.Cancel();

            await AwaitProxyTaskAsync(browserToHostTask);
            await AwaitProxyTaskAsync(hostToBrowserTask);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            closeDescription = "Browser disconnected.";
            _logger.LogDebug("Browser request aborted while terminal proxy session was active.");
        }
        catch (OperationCanceledException)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Closing terminal proxy session because the configured lifetime of {SessionLifetime} elapsed.",
                    proxyOptions.SessionLifetime.ToString());
            }
        }
        catch (EndOfStreamException exception)
        {
            closeDescription = "Terminal host session ended.";
            _logger.LogInformation(exception, "Terminal host closed the TN3270/TN3270E session.");
        }
        catch (WebSocketException exception)
        {
            _logger.LogWarning(exception, "WebSocket transport failed while proxying terminal traffic.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Terminal proxy session terminated unexpectedly.");

            await SendControlMessageSafeAsync(
                webSocket,
                new TerminalSessionErrorMessage(
                    "session-error",
                    "The terminal proxy session failed unexpectedly. Review the API logs for details."));
        }
        finally
        {
            await terminalService.DisconnectAsync(CancellationToken.None);

            await CloseWebSocketIfOpenAsync(
                webSocket,
                WebSocketCloseStatus.NormalClosure,
                closeDescription,
                CancellationToken.None);
        }
    }

    private static async Task AwaitProxyTaskAsync(Task proxyTask)
    {
        try
        {
            await proxyTask;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static byte[] EncodeFrame(Tn3270EFrame frame)
    {
        var encodedFrame = new byte[5 + frame.Data.Length];
        encodedFrame[0] = (byte)frame.DataType;
        encodedFrame[1] = frame.RequestFlag;
        encodedFrame[2] = frame.ResponseFlag;
        BinaryPrimitives.WriteUInt16BigEndian(encodedFrame.AsSpan(3, 2), frame.SequenceNumber);
        frame.Data.Span.CopyTo(encodedFrame.AsSpan(5));
        return encodedFrame;
    }

    private static Tn3270EFrame DecodeFrame(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 5)
        {
            throw new InvalidDataException(
                "Browser messages must contain at least the 5-byte TN3270E record header.");
        }

        var sequenceNumber = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(3, 2));

        return new Tn3270EFrame(
            (Tn3270EDataType)buffer[0],
            buffer[1],
            buffer[2],
            sequenceNumber,
            buffer[5..].ToArray());
    }

    private static async Task SendControlMessageAsync(
        WebSocket webSocket,
        object payload,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, _serializerOptions);
        var buffer = Encoding.UTF8.GetBytes(json);
        await webSocket.SendAsync(buffer, WebSocketMessageType.Text, true, cancellationToken);
    }

    private async Task SendControlMessageSafeAsync(WebSocket webSocket, object payload)
    {
        try
        {
            await SendControlMessageAsync(webSocket, payload, CancellationToken.None);
        }
        catch (Exception exception) when (exception is WebSocketException or ObjectDisposedException)
        {
            _logger.LogDebug(exception, "Failed to send terminal proxy control message because the socket is closing.");
        }
    }

    private static async Task CloseWebSocketIfOpenAsync(
        WebSocket webSocket,
        WebSocketCloseStatus closeStatus,
        string closeDescription,
        CancellationToken cancellationToken)
    {
        if (webSocket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
        {
            return;
        }

        await webSocket.CloseAsync(closeStatus, closeDescription, cancellationToken);
    }

    private static async Task<(WebSocketMessageType MessageType, byte[] Payload)> ReceiveMessageAsync(
        WebSocket webSocket,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        using var payload = new MemoryStream();

        while (true)
        {
            var result = await webSocket.ReceiveAsync(buffer, cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return (WebSocketMessageType.Close, []);
            }

            if (result.Count > 0)
            {
                await payload.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken);
            }

            if (result.EndOfMessage)
            {
                return (result.MessageType, payload.ToArray());
            }
        }
    }

    private async Task ProxyBrowserToHostAsync(
        WebSocket webSocket,
        ITn3270EService terminalService,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && webSocket.State == WebSocketState.Open)
        {
            var (messageType, payload) = await ReceiveMessageAsync(webSocket, cancellationToken);

            if (messageType == WebSocketMessageType.Close)
            {
                return;
            }

            if (messageType == WebSocketMessageType.Text)
            {
                _logger.LogDebug(
                    "Ignoring browser text message for terminal proxy session because only binary TN3270E frames are forwarded.");
                continue;
            }

            var frame = DecodeFrame(payload);
            await terminalService.SendAsync(frame, cancellationToken);
        }
    }

    private static async Task ProxyHostToBrowserAsync(
        WebSocket webSocket,
        ITn3270EService terminalService,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && webSocket.State == WebSocketState.Open)
        {
            var frame = await terminalService.ReceiveAsync(cancellationToken);
            var payload = EncodeFrame(frame);
            await webSocket.SendAsync(payload, WebSocketMessageType.Binary, true, cancellationToken);
        }
    }
}
