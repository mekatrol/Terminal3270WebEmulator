using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Terminal.Common.Models;
using Terminal.Common.Options;
using Terminal.Common.Services;

namespace Terminal.Console;

internal sealed partial class TerminalClientService(
    ITn3270EService terminalService,
    IOptions<Tn3270EOptions> options,
    ILogger<TerminalClientService> logger,
    IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var opts = options.Value;

            LogConnecting(logger, opts.Host, opts.Port, opts.TerminalType);

            await terminalService.ConnectAsync(opts.Host, opts.Port, stoppingToken);

            logger.LogInformation("TCP connection established, negotiating TN3270E session");

            var negotiated = await terminalService.NegotiateAsync(
                opts.TerminalType, opts.DeviceName, stoppingToken);

            if (!negotiated)
            {
                logger.LogError("TN3270E negotiation failed");
                return;
            }

            logger.LogInformation("TN3270E session established, entering data phase");

            while (!stoppingToken.IsCancellationRequested)
            {
                var frame = await terminalService.ReceiveAsync(stoppingToken);
                LogFrameReceived(logger, frame.DataType, frame.SequenceNumber, frame.Data.Length);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown path — not an error.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Terminal session ended with an error");
        }
        finally
        {
            await terminalService.DisconnectAsync(CancellationToken.None);
            lifetime.StopApplication();
        }
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Connecting to {Host}:{Port} with terminal type {TerminalType}")]
    private static partial void LogConnecting(ILogger logger, string host, int port, string terminalType);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Received frame: DataType={DataType} SeqNum={SeqNum} DataLength={Length}")]
    private static partial void LogFrameReceived(
        ILogger logger, Tn3270EDataType dataType, ushort seqNum, int length);
}
