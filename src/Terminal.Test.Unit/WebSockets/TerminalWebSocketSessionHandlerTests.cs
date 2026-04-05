using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Terminal.Api.Admin;
using Terminal.Api.Options;
using Terminal.Api.WebSockets;
using Terminal.Common.Models;
using Terminal.Common.Options;
using Terminal.Common.Services;
using Terminal.Data.Context;

namespace Terminal.Test.Unit.WebSockets;

/// <summary>
/// Verifies that the API WebSocket session handler tears down TN3270/TELNET
/// state when the browser transport disappears unexpectedly.
/// </summary>
[TestClass]
public sealed class TerminalWebSocketSessionHandlerTests
{
    /// <summary>
    /// Confirms that an abrupt browser-side WebSocket failure still causes the
    /// handler to cancel the proxy loop, disconnect the TN3270 transport, and
    /// mark the persisted session as closed.
    /// </summary>
    [TestMethod]
    public async Task HandleAsync_WhenBrowserTransportFails_CleansUpTrackedSessionAndHostConnection()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var fakeTerminalService = new FakeTn3270EService();

        var services = new ServiceCollection();
        services.AddScoped<ITn3270EService>(_ => fakeTerminalService);
        services.AddDbContext<TerminalDataContext>(options => options.UseInMemoryDatabase(databaseName));

        await using var serviceProvider = services.BuildServiceProvider();

        var handler = new TerminalWebSocketSessionHandler(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new TerminalProxyOptions
            {
                SessionLifetime = TimeSpan.FromMinutes(5),
                TerminalEndpointDisplayName = "PITS (Platform Integrated Terminal Server)",
                WebSocketKeepAliveInterval = TimeSpan.FromSeconds(15),
                WebSocketPath = "/ws/terminal",
            }),
            Options.Create(new Tn3270EOptions
            {
                Host = "mock-mainframe",
                Port = 23,
                TerminalType = "IBM-3278-2-E",
            }),
            new ActiveTerminalSessionRegistry(),
            NullLogger<TerminalWebSocketSessionHandler>.Instance);

        using var requestAbortedSource = new CancellationTokenSource();
        var webSocket = new AbruptDisconnectWebSocket(fakeTerminalService.ReceiveStarted);
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpWebSocketFeature>(new TestWebSocketFeature(webSocket));
        context.RequestAborted = requestAbortedSource.Token;
        context.User = BuildAuthenticatedUser();

        await handler.HandleAsync(context);

        Assert.IsTrue(fakeTerminalService.ConnectCalled, "The handler should connect to the TN3270 host.");
        Assert.IsTrue(fakeTerminalService.NegotiateCalled, "The handler should negotiate the terminal session.");
        Assert.IsTrue(
            fakeTerminalService.ReceiveCancellationObserved,
            "Host-to-browser proxying should be cancelled before teardown completes.");
        Assert.IsTrue(fakeTerminalService.DisconnectCalled, "The TN3270/TELNET transport should always disconnect.");

        await using var verificationScope = serviceProvider.CreateAsyncScope();
        var terminalDataContext = verificationScope.ServiceProvider.GetRequiredService<TerminalDataContext>();
        var terminalSessions = await terminalDataContext.TerminalSessions.ToListAsync();

        Assert.HasCount(1, terminalSessions, "Exactly one tracked session should have been persisted.");
        Assert.IsFalse(terminalSessions[0].IsActive, "The tracked session should be marked inactive after cleanup.");
        Assert.IsNotNull(terminalSessions[0].ClosedDateTimeUtc, "Cleanup should stamp the session close timestamp.");
    }

    /// <summary>
    /// Confirms that an administrative termination request emits a specific
    /// browser-visible control message before the socket closes so the terminal
    /// UI can explain why the session ended.
    /// </summary>
    [TestMethod]
    public async Task HandleAsync_WhenAdministratorTerminatesSession_SendsAdministratorEndedMessage()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var fakeTerminalService = new FakeTn3270EService();
        var sessionRegistry = new ActiveTerminalSessionRegistry();

        var services = new ServiceCollection();
        services.AddScoped<ITn3270EService>(_ => fakeTerminalService);
        services.AddDbContext<TerminalDataContext>(options => options.UseInMemoryDatabase(databaseName));

        await using var serviceProvider = services.BuildServiceProvider();

        var handler = new TerminalWebSocketSessionHandler(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new TerminalProxyOptions
            {
                SessionLifetime = TimeSpan.FromMinutes(5),
                TerminalEndpointDisplayName = "PITS (Platform Integrated Terminal Server)",
                WebSocketKeepAliveInterval = TimeSpan.FromSeconds(15),
                WebSocketPath = "/ws/terminal",
            }),
            Options.Create(new Tn3270EOptions
            {
                Host = "mock-mainframe",
                Port = 23,
                TerminalType = "IBM-3278-2-E",
            }),
            sessionRegistry,
            NullLogger<TerminalWebSocketSessionHandler>.Instance);

        var webSocket = new RecordingWebSocket();
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpWebSocketFeature>(new TestWebSocketFeature(webSocket));
        context.RequestAborted = CancellationToken.None;
        context.User = BuildAuthenticatedUser();

        var handleTask = handler.HandleAsync(context);

        await fakeTerminalService.ReceiveStarted.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitForTrackedSessionAsync(serviceProvider, 1);

        await using (var verificationScope = serviceProvider.CreateAsyncScope())
        {
            var terminalDataContext = verificationScope.ServiceProvider.GetRequiredService<TerminalDataContext>();
            var trackedSession = await terminalDataContext.TerminalSessions.SingleAsync();
            Assert.IsTrue(await sessionRegistry.TryRequestTerminationAsync(trackedSession.TerminalSessionId));
        }

        await handleTask;

        Assert.IsTrue(
            webSocket.TextMessages.Any(message =>
                MessageHasSessionEndedReason(message, "administrator-terminated", null)),
            "The browser should receive an administrator-specific session-ended control message.");
    }

    /// <summary>
    /// Confirms that a host-side TN3270/TELNET disconnect emits a control
    /// message naming the configured terminal endpoint so the SPA can explain
    /// which remote system ended the session.
    /// </summary>
    [TestMethod]
    public async Task HandleAsync_WhenEndpointServerEndsSession_SendsEndpointDisplayName()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var fakeTerminalService = new FakeTn3270EService
        {
            ReceiveException = new EndOfStreamException("Mock terminal host ended the session."),
        };

        var services = new ServiceCollection();
        services.AddScoped<ITn3270EService>(_ => fakeTerminalService);
        services.AddDbContext<TerminalDataContext>(options => options.UseInMemoryDatabase(databaseName));

        await using var serviceProvider = services.BuildServiceProvider();

        var handler = new TerminalWebSocketSessionHandler(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new TerminalProxyOptions
            {
                SessionLifetime = TimeSpan.FromMinutes(5),
                TerminalEndpointDisplayName = "PITS (Platform Integrated Terminal Server)",
                WebSocketKeepAliveInterval = TimeSpan.FromSeconds(15),
                WebSocketPath = "/ws/terminal",
            }),
            Options.Create(new Tn3270EOptions
            {
                Host = "mock-mainframe",
                Port = 23,
                TerminalType = "IBM-3278-2-E",
            }),
            new ActiveTerminalSessionRegistry(),
            NullLogger<TerminalWebSocketSessionHandler>.Instance);

        var webSocket = new RecordingWebSocket();
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpWebSocketFeature>(new TestWebSocketFeature(webSocket));
        context.RequestAborted = CancellationToken.None;
        context.User = BuildAuthenticatedUser();

        await handler.HandleAsync(context);

        Assert.IsTrue(
            webSocket.TextMessages.Any(message =>
                MessageHasSessionEndedReason(
                    message,
                    "endpoint-server-terminated",
                    "PITS (Platform Integrated Terminal Server)")),
            "The browser should receive a host-specific session-ended control message.");
    }

    private static async Task WaitForTrackedSessionAsync(ServiceProvider serviceProvider, int expectedCount)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            await using var verificationScope = serviceProvider.CreateAsyncScope();
            var terminalDataContext = verificationScope.ServiceProvider.GetRequiredService<TerminalDataContext>();
            if (await terminalDataContext.TerminalSessions.CountAsync() == expectedCount)
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.Fail("Timed out waiting for the tracked terminal session to be persisted.");
    }

    private static bool MessageHasSessionEndedReason(
        string message,
        string expectedReason,
        string? expectedTerminalEndpointDisplayName)
    {
        using var document = JsonDocument.Parse(message);
        var root = document.RootElement;

        if (!root.TryGetProperty("type", out var typeProperty) ||
            typeProperty.GetString() != "session-ended")
        {
            return false;
        }

        if (!root.TryGetProperty("reason", out var reasonProperty) ||
            reasonProperty.GetString() != expectedReason)
        {
            return false;
        }

        if (!root.TryGetProperty("terminalEndpointDisplayName", out var displayNameProperty))
        {
            return false;
        }

        return displayNameProperty.GetString() == expectedTerminalEndpointDisplayName;
    }

    private static ClaimsPrincipal BuildAuthenticatedUser()
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "user-123"),
            new Claim("preferred_username", "serveradmin@mockidp.local"),
        ],
            authenticationType: "TestAuth");

        return new ClaimsPrincipal(identity);
    }

    /// <summary>
    /// Minimal WebSocket feature that reports an upgrade request and returns a
    /// preconstructed <see cref="WebSocket"/> instance when accepted.
    /// </summary>
    private sealed class TestWebSocketFeature(WebSocket webSocket) : IHttpWebSocketFeature
    {
        public bool IsWebSocketRequest => true;

        public Task<WebSocket> AcceptAsync(WebSocketAcceptContext context)
            => Task.FromResult(webSocket);
    }

    /// <summary>
    /// WebSocket double that simulates a browser process disappearing before it
    /// sends a close frame.
    /// </summary>
    private sealed class AbruptDisconnectWebSocket(Task receiveStarted) : WebSocket
    {
        private WebSocketState _state = WebSocketState.Open;

        public override WebSocketCloseStatus? CloseStatus => null;

        public override string? CloseStatusDescription => null;

        public override WebSocketState State => _state;

        public override string SubProtocol => string.Empty;

        public override void Abort()
        {
            _state = WebSocketState.Aborted;
        }

        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public override void Dispose()
        {
            _state = WebSocketState.Closed;
        }

        public override async Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken)
        {
            await receiveStarted.WaitAsync(cancellationToken);
            _state = WebSocketState.Aborted;
            throw new WebSocketException(WebSocketError.ConnectionClosedPrematurely);
        }

        public override Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    /// <summary>
    /// WebSocket double that records outbound text control messages while
    /// keeping the browser-to-host receive loop open until the handler closes
    /// the session.
    /// </summary>
    private sealed class RecordingWebSocket : WebSocket
    {
        private readonly List<string> _textMessages = [];
        private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private WebSocketState _state = WebSocketState.Open;

        public IReadOnlyList<string> TextMessages => _textMessages;

        public override WebSocketCloseStatus? CloseStatus => null;

        public override string? CloseStatusDescription => null;

        public override WebSocketState State => _state;

        public override string SubProtocol => string.Empty;

        public override void Abort()
        {
            _state = WebSocketState.Aborted;
            _ = _closed.TrySetResult();
        }

        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            _state = WebSocketState.Closed;
            _ = _closed.TrySetResult();
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public override void Dispose()
        {
            _state = WebSocketState.Closed;
            _ = _closed.TrySetResult();
        }

        public override async Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken)
        {
            await _closed.Task.WaitAsync(cancellationToken);
            return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true);
        }

        public override Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            if (messageType == WebSocketMessageType.Text)
            {
                _textMessages.Add(Encoding.UTF8.GetString(buffer));
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// TN3270 service double that blocks the host-to-browser receive loop until
    /// the handler cancels it during cleanup.
    /// </summary>
    private sealed class FakeTn3270EService : ITn3270EService
    {
        public bool IsConnected { get; private set; }

        public string? ConnectedHost { get; private set; }

        public int ConnectedPort { get; private set; }

        public bool ConnectCalled { get; private set; }

        public bool NegotiateCalled { get; private set; }

        public bool DisconnectCalled { get; private set; }

        public bool ReceiveCancellationObserved { get; private set; }

        public Task ReceiveStarted => _receiveStarted.Task;

        private readonly TaskCompletionSource _receiveStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Exception? ReceiveException { get; set; }

        public Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
        {
            ConnectCalled = true;
            IsConnected = true;
            ConnectedHost = host;
            ConnectedPort = port;
            return Task.CompletedTask;
        }

        public Task<bool> NegotiateAsync(
            string terminalType,
            string? deviceName = null,
            CancellationToken cancellationToken = default)
        {
            NegotiateCalled = true;
            return Task.FromResult(true);
        }

        public Task SendAsync(Tn3270EFrame frame, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public async Task<Tn3270EFrame> ReceiveAsync(CancellationToken cancellationToken = default)
        {
            _ = _receiveStarted.TrySetResult();

            if (ReceiveException is not null)
            {
                throw ReceiveException;
            }

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The receive loop should be cancelled before data arrives.");
            }
            catch (OperationCanceledException)
            {
                ReceiveCancellationObserved = true;
                throw;
            }
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            DisconnectCalled = true;
            IsConnected = false;
            ConnectedHost = null;
            ConnectedPort = 0;
            return Task.CompletedTask;
        }
    }
}
