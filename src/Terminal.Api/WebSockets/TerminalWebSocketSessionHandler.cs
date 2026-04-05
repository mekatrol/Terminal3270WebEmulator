using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Buffers.Binary;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Terminal.Api.Admin;
using Terminal.Api.Options;
using Terminal.Common.Models;
using Terminal.Common.Options;
using Terminal.Common.Services;
using Terminal.Data.Context;
using Terminal.Data.Models;

namespace Terminal.Api.WebSockets;

internal sealed class TerminalWebSocketSessionHandler(
    IServiceScopeFactory serviceScopeFactory,
    IOptions<TerminalProxyOptions> terminalProxyOptions,
    IOptions<Tn3270EOptions> tn3270EOptions,
    IOptions<OidcAuthenticationOptions> authenticationOptions,
    ActiveTerminalSessionRegistry sessionRegistry,
    ILogger<TerminalWebSocketSessionHandler> logger)
{
    private static readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);
    private const string _terminalRolePrefix = "Terminal.";
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;
    private readonly IOptions<TerminalProxyOptions> _terminalProxyOptions = terminalProxyOptions;
    private readonly IOptions<Tn3270EOptions> _tn3270EOptions = tn3270EOptions;
    private readonly IOptions<OidcAuthenticationOptions> _authenticationOptions = authenticationOptions;
    private readonly ActiveTerminalSessionRegistry _sessionRegistry = sessionRegistry;
    private readonly ILogger<TerminalWebSocketSessionHandler> _logger = logger;

    public async Task HandleAsync(HttpContext context)
    {
        if (!CanStartTerminalSession(context.User))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync(
                "You do not have permission to open a terminal session. A role starting with 'Terminal.' is required.");
            return;
        }

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
        var terminalDataContext = scope.ServiceProvider.GetRequiredService<TerminalDataContext>();

        using var sessionCancellationSource =
            CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        sessionCancellationSource.CancelAfter(proxyOptions.SessionLifetime);
        var closeDescription = "Terminal proxy session closed.";
        Task? browserToHostTask = null;
        Task? hostToBrowserTask = null;
        var trackedSession = await CreateTrackedSessionAsync(
            context,
            terminalDataContext,
            sessionCancellationSource.Token);

        if (trackedSession is not null)
        {
            _sessionRegistry.Register(
                trackedSession.TerminalSessionId,
                sessionCancellationSource,
                () => NotifyAdministratorTerminationAsync(webSocket));
        }

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

            browserToHostTask = ProxyBrowserToHostAsync(
                webSocket,
                terminalService,
                sessionCancellationSource.Token);
            hostToBrowserTask = ProxyHostToBrowserAsync(
                webSocket,
                terminalService,
                sessionCancellationSource.Token);

            var completedProxyTask = await Task.WhenAny(browserToHostTask, hostToBrowserTask);
            await completedProxyTask;
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            closeDescription = "Browser disconnected.";
            _logger.LogDebug("Browser request aborted while terminal proxy session was active.");
        }
        catch (OperationCanceledException)
        {
            if (trackedSession is not null &&
                _sessionRegistry.WasTerminationRequested(trackedSession.TerminalSessionId))
            {
                closeDescription = "Terminal session terminated by administrator.";

                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation(
                        "Terminal proxy session {TerminalSessionId} was terminated by an administrator.",
                        trackedSession.TerminalSessionId);
                }

                await SendControlMessageSafeAsync(
                    webSocket,
                    new TerminalSessionEndedMessage(
                        "session-ended",
                        "administrator-terminated",
                        null));
            }
            else if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Closing terminal proxy session because the configured lifetime of {SessionLifetime} elapsed.",
                    proxyOptions.SessionLifetime.ToString());
            }
        }
        catch (EndOfStreamException exception)
        {
            closeDescription = $"Terminal session terminated by {proxyOptions.TerminalEndpointDisplayName}.";
            _logger.LogInformation(exception, "Terminal host closed the TN3270/TN3270E session.");

            await SendControlMessageSafeAsync(
                webSocket,
                new TerminalSessionEndedMessage(
                    "session-ended",
                    "endpoint-server-terminated",
                    proxyOptions.TerminalEndpointDisplayName));
        }
        catch (WebSocketException exception)
        {
            closeDescription = "Browser disconnected unexpectedly.";
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
            await CancelAndAwaitProxyTasksAsync(
                sessionCancellationSource,
                browserToHostTask,
                hostToBrowserTask);

            await terminalService.DisconnectAsync(CancellationToken.None);

            if (trackedSession is not null)
            {
                _sessionRegistry.Unregister(trackedSession.TerminalSessionId);
            }

            await CompleteTrackedSessionAsync(
                trackedSession,
                terminalDataContext,
                CancellationToken.None);

            await CloseWebSocketIfOpenAsync(
                webSocket,
                WebSocketCloseStatus.NormalClosure,
                closeDescription,
                CancellationToken.None);
        }
    }

    private async Task CancelAndAwaitProxyTasksAsync(
        CancellationTokenSource sessionCancellationSource,
        Task? browserToHostTask,
        Task? hostToBrowserTask)
    {
        if (!sessionCancellationSource.IsCancellationRequested)
        {
            sessionCancellationSource.Cancel();
        }

        if (browserToHostTask is not null)
        {
            await AwaitProxyTaskAsync(browserToHostTask);
        }

        if (hostToBrowserTask is not null)
        {
            await AwaitProxyTaskAsync(hostToBrowserTask);
        }
    }

    private async Task AwaitProxyTaskAsync(Task proxyTask)
    {
        try
        {
            await proxyTask;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Proxy loop cancellation was observed during session shutdown.");
        }
        catch (ObjectDisposedException)
        {
            _logger.LogDebug("Proxy loop observed a disposed transport while the session was shutting down.");
        }
        catch (WebSocketException)
        {
            _logger.LogDebug("Proxy loop observed a WebSocket transport failure during shutdown.");
        }
        catch (EndOfStreamException)
        {
            _logger.LogDebug("Proxy loop observed the TN3270/TN3270E stream closing during shutdown.");
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

    private async Task NotifyAdministratorTerminationAsync(WebSocket webSocket)
    {
        await SendControlMessageSafeAsync(
            webSocket,
            new TerminalSessionEndedMessage(
                "session-ended",
                "administrator-terminated",
                null));
    }

    private async Task<TerminalSession?> CreateTrackedSessionAsync(
        HttpContext context,
        TerminalDataContext terminalDataContext,
        CancellationToken cancellationToken)
    {
        if (ResolveTrackedUser(context.User) is not { } userIdentity)
        {
            _logger.LogDebug(
                "Skipping terminal session persistence because the request does not carry an authenticated user identity.");
            return null;
        }

        try
        {
            var user = await terminalDataContext.Users.SingleOrDefaultAsync(
                existingUser => existingUser.UserId == userIdentity.UserId,
                cancellationToken);

            if (user is null)
            {
                user = new User
                {
                    UserId = userIdentity.UserId,
                    UserName = userIdentity.UserName,
                };

                terminalDataContext.Users.Add(user);
            }
            else
            {
                user.UserName = userIdentity.UserName;
            }

            var session = new TerminalSession
            {
                TerminalSessionId = Guid.NewGuid(),
                CreatedDateTimeUtc = DateTimeOffset.UtcNow,
                IsActive = true,
                UserId = user.UserId,
            };

            terminalDataContext.TerminalSessions.Add(session);
            await terminalDataContext.SaveChangesAsync(cancellationToken);
            return session;
        }
        catch (Exception exception)
        {
            // Session tracking is observability state. Logging and continuing keeps
            // terminal access available even if the in-memory store is misconfigured.
            _logger.LogError(exception, "Failed to persist the opening terminal session record.");
            return null;
        }
    }

    private async Task CompleteTrackedSessionAsync(
        TerminalSession? trackedSession,
        TerminalDataContext terminalDataContext,
        CancellationToken cancellationToken)
    {
        if (trackedSession is null)
        {
            return;
        }

        try
        {
            trackedSession.IsActive = false;
            trackedSession.ClosedDateTimeUtc = DateTimeOffset.UtcNow;
            await terminalDataContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            // Cleanup failures must be logged, but they should not prevent the
            // network session from closing because the client has already ended.
            _logger.LogError(exception, "Failed to persist the terminal session close record.");
        }
    }

    private static (string UserId, string UserName)? ResolveTrackedUser(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var userId =
            principal.FindFirstValue(ClaimTypes.NameIdentifier) ??
            principal.FindFirstValue("oid") ??
            principal.FindFirstValue("sub") ??
            principal.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier") ??
            principal.FindFirstValue("preferred_username") ??
            principal.FindFirstValue(ClaimTypes.Email) ??
            principal.Identity.Name;

        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        var userName =
            principal.FindFirstValue("preferred_username") ??
            principal.FindFirstValue(ClaimTypes.Name) ??
            principal.FindFirstValue(ClaimTypes.Email) ??
            principal.Identity.Name ??
            userId;

        return (userId, userName);
    }

    private bool CanStartTerminalSession(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var configuredTerminalRoles = new[]
        {
            _authenticationOptions.Value.TerminalUserRole,
            _authenticationOptions.Value.TerminalAdminRole,
        };

        return configuredTerminalRoles.Any(role => !string.IsNullOrWhiteSpace(role) && principal.IsInRole(role)) ||
            principal.Claims.Any(claim =>
                string.Equals(claim.Type, "roles", StringComparison.Ordinal) &&
                claim.Value.StartsWith(_terminalRolePrefix, StringComparison.Ordinal));
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
