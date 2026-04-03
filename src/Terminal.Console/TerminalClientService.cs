using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Terminal.Common.Models;
using Terminal.Common.Options;
using Terminal.Common.Protocol;
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
                LogFrameDetails(logger, frame);
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

    private static void LogFrameDetails(ILogger logger, Tn3270EFrame frame)
    {
        if (frame.DataType != Tn3270EDataType.Data3270)
        {
            LogNon3270FrameReceived(
                logger,
                frame.DataType,
                frame.SequenceNumber,
                frame.RequestFlag,
                frame.ResponseFlag);
            return;
        }

        var description = Tn3270DataStreamParser.Describe(frame.Data.Span);

        LogRecordReceived(
            logger,
            description.CommandName,
            description.CommandCode,
            frame.SequenceNumber);

        foreach (var message in description.Messages)
        {
            LogRecordDetail(logger, message);
        }
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "TN3270E message type {DataType} SeqNum={SeqNum} Request=0x{RequestFlag:X2} Response=0x{ResponseFlag:X2}")]
    private static partial void LogNon3270FrameReceived(
        ILogger logger,
        Tn3270EDataType dataType,
        ushort seqNum,
        byte requestFlag,
        byte responseFlag);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "3270 record: {CommandName} (0x{CommandCode:X2}) SeqNum={SeqNum}")]
    private static partial void LogRecordReceived(
        ILogger logger,
        string commandName,
        byte commandCode,
        ushort seqNum);

    [LoggerMessage(Level = LogLevel.Information, Message = "3270 detail: {Message}")]
    private static partial void LogRecordDetail(ILogger logger, string message);
}
