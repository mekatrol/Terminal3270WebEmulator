using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Sockets;
using Terminal.MockServer.Screens;

namespace Terminal.MockServer.Services;

/// <summary>
/// Listens for incoming TCP connections and spawns a <see cref="MockSessionHandler"/> for each one.
/// </summary>
internal sealed partial class MockTn3270Server(
    IOptions<MockServerOptions> options,
    ScreenRegistry registry,
    ILogger<MockTn3270Server> logger,
    ILogger<MockSessionHandler> sessionLogger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        var endpoint = new IPEndPoint(IPAddress.Parse(opts.Host), opts.Port);
        using var listener = new TcpListener(endpoint);
        listener.Start();
        LogListening(logger, opts.Host, opts.Port);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                var remote = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
                LogClientConnected(logger, remote);

                // Fire-and-forget: each session runs on its own task so the listener
                // can continue accepting new connections without waiting.
                _ = RunSessionAsync(client, remote, opts, stoppingToken);
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task RunSessionAsync(
        TcpClient client,
        string remote,
        MockServerOptions opts,
        CancellationToken stoppingToken)
    {
        using (client)
        {
            try
            {
                var handler = new MockSessionHandler(
                    client.GetStream(),
                    registry,
                    opts,
                    sessionLogger);

                await handler.RunAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LogSessionError(logger, remote, ex);
            }
            finally
            {
                LogClientDisconnected(logger, remote);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "TN3270E mock server listening on {Host}:{Port}")]
    private static partial void LogListening(ILogger logger, string host, int port);

    [LoggerMessage(Level = LogLevel.Information, Message = "Client connected: {Remote}")]
    private static partial void LogClientConnected(ILogger logger, string remote);

    [LoggerMessage(Level = LogLevel.Information, Message = "Client disconnected: {Remote}")]
    private static partial void LogClientDisconnected(ILogger logger, string remote);

    [LoggerMessage(Level = LogLevel.Error, Message = "Session error for {Remote}")]
    private static partial void LogSessionError(ILogger logger, string remote, Exception ex);
}
