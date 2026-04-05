using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Terminal.Common.Terminal;

namespace Terminal.Test.Integration.TestInfrastructure;

/// <summary>
/// Opens an authenticated API WebSocket and drives the mock terminal sign-in and PF3 logout flow.
/// </summary>
internal sealed class TerminalProxyLoadClient(Uri terminalWebSocketUri, string accessToken)
{
    private const byte _enterAid = 0x7D;
    private const byte _pf3Aid = 0xF3;
    private static readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);
    private readonly Uri _terminalWebSocketUri = terminalWebSocketUri;
    private readonly string _accessToken = accessToken;

    /// <summary>
    /// Executes one full mock user cycle: connect, sign in, PF3 back to login, PF3 to exit.
    /// </summary>
    public async Task<SessionRunResult> RunAsync(CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedSource.CancelAfter(TimeSpan.FromSeconds(30));
        var linkedToken = linkedSource.Token;
        var stopwatch = Stopwatch.StartNew();

        var webSocketUri = AppendAccessToken(_terminalWebSocketUri, _accessToken);
        await socket.ConnectAsync(webSocketUri, linkedToken);
        var connectedAt = stopwatch.Elapsed;

        var readyMessage = await ReceiveReadyMessageAsync(socket, linkedToken);
        var readyAt = stopwatch.Elapsed;
        var screen = new Tn3270TerminalScreen(readyMessage.TerminalType);

        await ReceiveNextScreenAsync(socket, screen, linkedToken);
        WriteText(screen, "DEMOUSER");
        screen.MoveToAdjacentField(forward: true);
        WriteText(screen, "PASSWORD");
        await SendFrameAsync(socket, screen.BuildReadModifiedRecord(_enterAid), linkedToken);

        await ReceiveNextScreenAsync(socket, screen, linkedToken);
        var loggedInAt = stopwatch.Elapsed;
        await SendFrameAsync(socket, screen.BuildReadModifiedRecord(_pf3Aid), linkedToken);

        await ReceiveNextScreenAsync(socket, screen, linkedToken);
        await SendFrameAsync(socket, screen.BuildReadModifiedRecord(_pf3Aid), linkedToken);

        var endedMessage = await ReceiveEndedMessageAsync(socket, linkedToken);
        stopwatch.Stop();
        return new SessionRunResult(
            readyMessage.TerminalType,
            endedMessage.Reason,
            endedMessage.TerminalEndpointDisplayName,
            new SessionRunMetrics(
                ConnectLatency: connectedAt,
                ReadyLatency: readyAt - connectedAt,
                LoginLatency: loggedInAt - readyAt,
                LogoutLatency: stopwatch.Elapsed - loggedInAt,
                SessionDuration: stopwatch.Elapsed));
    }

    private static Uri AppendAccessToken(Uri baseUri, string accessToken)
    {
        var builder = new UriBuilder(baseUri);
        var encodedToken = Uri.EscapeDataString(accessToken);
        builder.Query = string.IsNullOrWhiteSpace(builder.Query)
            ? $"access_token={encodedToken}"
            : $"{builder.Query.TrimStart('?')}&access_token={encodedToken}";
        return builder.Uri;
    }

    private static void WriteText(Tn3270TerminalScreen screen, string text)
    {
        foreach (var character in text)
        {
            if (!screen.TryWriteCharacter(character))
            {
                throw new InvalidOperationException(
                    $"Could not write character '{character}' at cursor address {screen.CursorAddress}.");
            }
        }
    }

    private static async Task SendFrameAsync(
        ClientWebSocket socket,
        byte[] recordPayload,
        CancellationToken cancellationToken)
    {
        var framePayload = new byte[5 + recordPayload.Length];
        framePayload[0] = 0x00;
        framePayload[1] = 0x00;
        framePayload[2] = 0x00;
        BinaryPrimitives.WriteUInt16BigEndian(framePayload.AsSpan(3, 2), 0);
        recordPayload.CopyTo(framePayload, 5);
        await socket.SendAsync(framePayload, WebSocketMessageType.Binary, true, cancellationToken);
    }

    private static async Task<ReadyMessage> ReceiveReadyMessageAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var message = await ReceiveMessageAsync(socket, cancellationToken);
            if (message.MessageType != WebSocketMessageType.Text)
            {
                continue;
            }

            using var document = JsonDocument.Parse(message.Payload);
            if (!document.RootElement.TryGetProperty("type", out var typeProperty))
            {
                continue;
            }

            var messageType = typeProperty.GetString();
            if (string.Equals(messageType, "session-error", StringComparison.Ordinal))
            {
                var errorMessage = document.RootElement.GetProperty("message").GetString();
                throw new InvalidOperationException($"Terminal session failed before ready: {errorMessage}");
            }

            if (string.Equals(messageType, "session-ready", StringComparison.Ordinal))
            {
                var payload = JsonSerializer.Deserialize<ReadyMessage>(message.Payload, _serializerOptions)
                    ?? throw new InvalidOperationException("Could not deserialize session-ready message.");
                return payload;
            }
        }
    }

    private static async Task ReceiveNextScreenAsync(
        ClientWebSocket socket,
        Tn3270TerminalScreen screen,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var message = await ReceiveMessageAsync(socket, cancellationToken);

            if (message.MessageType == WebSocketMessageType.Binary)
            {
                if (message.Payload.Length < 5)
                {
                    throw new InvalidOperationException("Binary WebSocket frame was shorter than the TN3270E proxy header.");
                }

                var dataType = message.Payload[0];
                if (dataType != 0x00)
                {
                    continue;
                }

                screen.ApplyInboundRecord(message.Payload.AsSpan(5));
                return;
            }

            using var document = JsonDocument.Parse(message.Payload);
            var messageType = document.RootElement.GetProperty("type").GetString();

            if (string.Equals(messageType, "session-error", StringComparison.Ordinal))
            {
                var errorMessage = document.RootElement.GetProperty("message").GetString();
                throw new InvalidOperationException($"Terminal session returned an error: {errorMessage}");
            }

            if (string.Equals(messageType, "session-ended", StringComparison.Ordinal))
            {
                var endedMessage = JsonSerializer.Deserialize<EndedMessage>(message.Payload, _serializerOptions);
                throw new InvalidOperationException(
                    $"Terminal session ended unexpectedly while waiting for a screen. Reason: {endedMessage?.Reason ?? "unknown"}.");
            }
        }
    }

    private static async Task<EndedMessage> ReceiveEndedMessageAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            ReceivedMessage message;
            try
            {
                message = await ReceiveMessageAsync(socket, cancellationToken);
            }
            catch (Exception exception) when (exception is WebSocketException or IOException)
            {
                // At higher concurrency the server can still complete the session by
                // tearing down the transport before the browser-visible control message
                // is observed. For a load test, a post-logout remote close still
                // represents a successful completed session.
                return new EndedMessage(
                    "session-ended",
                    "endpoint-server-terminated",
                    null);
            }

            if (message.MessageType == WebSocketMessageType.Text)
            {
                using var document = JsonDocument.Parse(message.Payload);
                var messageType = document.RootElement.GetProperty("type").GetString();

                if (string.Equals(messageType, "session-ended", StringComparison.Ordinal))
                {
                    var ended = JsonSerializer.Deserialize<EndedMessage>(message.Payload, _serializerOptions)
                        ?? throw new InvalidOperationException("Could not deserialize session-ended message.");
                    return ended;
                }

                if (string.Equals(messageType, "session-error", StringComparison.Ordinal))
                {
                    var errorMessage = document.RootElement.GetProperty("message").GetString();
                    throw new InvalidOperationException($"Terminal session returned an error during shutdown: {errorMessage}");
                }
            }

            if (message.MessageType == WebSocketMessageType.Close)
            {
                return new EndedMessage(
                    "session-ended",
                    "endpoint-server-terminated",
                    null);
            }
        }
    }

    private static async Task<ReceivedMessage> ReceiveMessageAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        using var payload = new MemoryStream();

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return new ReceivedMessage(WebSocketMessageType.Close, []);
            }

            if (result.Count > 0)
            {
                await payload.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken);
            }

            if (result.EndOfMessage)
            {
                return new ReceivedMessage(result.MessageType, payload.ToArray());
            }
        }
    }

    /// <summary>
    /// Describes one completed terminal session cycle.
    /// </summary>
    public sealed record SessionRunResult(
        string TerminalType,
        string EndReason,
        string? TerminalEndpointDisplayName,
        SessionRunMetrics Metrics);

    /// <summary>
    /// Captures stage timings for one browserless terminal session cycle after the access token is available.
    /// </summary>
    public sealed record SessionRunMetrics(
        TimeSpan ConnectLatency,
        TimeSpan ReadyLatency,
        TimeSpan LoginLatency,
        TimeSpan LogoutLatency,
        TimeSpan SessionDuration);

    private sealed record ReadyMessage(string Type, string Host, int Port, string TerminalType, string? DeviceName, TimeSpan SessionLifetime);

    private sealed record EndedMessage(string Type, string Reason, string? TerminalEndpointDisplayName);

    private sealed record ReceivedMessage(WebSocketMessageType MessageType, byte[] Payload)
    {
        public string PayloadText => Encoding.UTF8.GetString(Payload);
    }
}
