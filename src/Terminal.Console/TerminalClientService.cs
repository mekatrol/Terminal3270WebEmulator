using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Terminal.Common.Models;
using Terminal.Common.Options;
using Terminal.Common.Protocol;
using Terminal.Common.Services;
using Terminal.Common.Terminal;

namespace Terminal.Console;

internal sealed partial class TerminalClientService(
    ITn3270EService terminalService,
    IOptions<Tn3270EOptions> options,
    ILogger<TerminalClientService> logger,
    IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var renderer = new TerminalConsoleRenderer();
        var sync = new object();

        try
        {
            var opts = options.Value;
            var screen = new Tn3270TerminalScreen(opts.TerminalType);

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

            lock (sync)
            {
                renderer.Initialise(screen);
                renderer.Render(screen);
            }

            var receiveTask = RunReceiveLoopAsync(screen, renderer, sync, stoppingToken);
            var inputTask = RunInputLoopAsync(screen, renderer, sync, stoppingToken);

            await Task.WhenAny(receiveTask, inputTask);
            await Task.WhenAll(receiveTask, inputTask);
        }
        catch (OperationCanceledException)
        {
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

    private async Task RunReceiveLoopAsync(
        Tn3270TerminalScreen screen,
        TerminalConsoleRenderer renderer,
        object sync,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var frame = await terminalService.ReceiveAsync(cancellationToken);
            LogFrameReceived(logger, frame.DataType, frame.SequenceNumber, frame.Data.Length);
            LogFrameDetails(logger, frame);

            if (frame.DataType != Tn3270EDataType.Data3270)
            {
                continue;
            }

            lock (sync)
            {
                screen.ApplyInboundRecord(frame.Data.Span);
                renderer.Render(screen);
            }
        }
    }

    private async Task RunInputLoopAsync(
        Tn3270TerminalScreen screen,
        TerminalConsoleRenderer renderer,
        object sync,
        CancellationToken cancellationToken)
    {
        if (global::System.Console.IsInputRedirected)
        {
            logger.LogWarning(
                "Console input is redirected, so local keyboard entry is disabled for this session.");
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            bool keyAvailable;
            try
            {
                keyAvailable = global::System.Console.KeyAvailable;
            }
            catch (InvalidOperationException)
            {
                logger.LogWarning(
                    "Console keyboard polling is unavailable in the current host, so local keyboard entry is disabled.");
                return;
            }
            catch (PlatformNotSupportedException)
            {
                logger.LogWarning(
                    "Console keyboard polling is not supported on this platform, so local keyboard entry is disabled.");
                return;
            }

            if (!keyAvailable)
            {
                await Task.Delay(25, cancellationToken);
                continue;
            }

            ConsoleKeyInfo keyInfo;
            try
            {
                keyInfo = global::System.Console.ReadKey(intercept: true);
            }
            catch (InvalidOperationException)
            {
                logger.LogWarning(
                    "Console key reads are unavailable in the current host, so local keyboard entry is disabled.");
                return;
            }
            catch (PlatformNotSupportedException)
            {
                logger.LogWarning(
                    "Console key reads are not supported on this platform, so local keyboard entry is disabled.");
                return;
            }

            byte[]? payload = null;
            var shouldRender = false;

            lock (sync)
            {
                switch (keyInfo.Key)
                {
                    case ConsoleKey.Tab:
                        shouldRender = screen.MoveToAdjacentField(
                            forward: (keyInfo.Modifiers & ConsoleModifiers.Shift) == 0);
                        break;

                    case ConsoleKey.LeftArrow:
                        shouldRender = screen.MoveCursor(-1, 0);
                        break;

                    case ConsoleKey.RightArrow:
                        shouldRender = screen.MoveCursor(1, 0);
                        break;

                    case ConsoleKey.UpArrow:
                        shouldRender = screen.MoveCursor(0, -1);
                        break;

                    case ConsoleKey.DownArrow:
                        shouldRender = screen.MoveCursor(0, 1);
                        break;

                    case ConsoleKey.Backspace:
                        shouldRender = screen.Backspace();
                        break;

                    case ConsoleKey.Delete:
                        shouldRender = screen.Delete();
                        break;

                    case ConsoleKey.Enter:
                        payload = screen.BuildReadModifiedRecord();
                        break;

                    case ConsoleKey.Escape:
                        lifetime.StopApplication();
                        return;

                    default:
                        if (!char.IsControl(keyInfo.KeyChar))
                        {
                            shouldRender = screen.TryWriteCharacter(keyInfo.KeyChar);
                        }

                        break;
                }

                if (shouldRender)
                {
                    renderer.Render(screen);
                }
            }

            if (payload is not null)
            {
                await terminalService.SendAsync(
                    new Tn3270EFrame(Tn3270EDataType.Data3270, 0x00, 0x00, 0, payload),
                    cancellationToken);
            }
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
