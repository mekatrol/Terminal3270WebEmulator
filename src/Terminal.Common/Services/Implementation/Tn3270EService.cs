using Microsoft.Extensions.Logging;
using Terminal.Common.Models;
using Terminal.Common.Protocol;

namespace Terminal.Common.Services.Implementation;

internal sealed partial class Tn3270EService(
    INetworkConnectionFactory connectionFactory,
    ILogger<Tn3270EService> logger) : ITn3270EService, IAsyncDisposable
{
    private enum SessionMode
    {
        Unknown,
        Tn3270,
        Tn3270E,
    }

    private enum NegotiationResult
    {
        Continue,
        Succeeded,
        Failed,
    }

    private Stream? _stream;
    private string? _connectedHost;
    private int _connectedPort;
    private bool _disposed;
    private SessionMode _sessionMode;

    // Buffer for bytes read from the stream but not yet processed.
    private readonly List<byte> _readBuffer = [];

    public bool IsConnected => _stream is not null;
    public string? ConnectedHost => _connectedHost;
    public int ConnectedPort => _connectedPort;

    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_stream is not null)
        {
            throw new InvalidOperationException("Already connected. Call DisconnectAsync first.");
        }

        LogOpeningConnection(logger, host, port);

        _stream = await connectionFactory.ConnectAsync(host, port, cancellationToken);
        _connectedHost = host;
        _connectedPort = port;
        _sessionMode = SessionMode.Unknown;

        LogConnectionOpen(logger, host, port);
    }

    public async Task<bool> NegotiateAsync(
        string terminalType,
        string? deviceName = null,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        const int maxNegotiationBytes = 4096;
        var scanned = 0;

        try
        {
            while (scanned < maxNegotiationBytes)
            {
                var b = await ReadByteAsync(cancellationToken);
                scanned++;

                if (b != TelnetConstants.Iac)
                {
                    _readBuffer.Insert(0, b);
                    _sessionMode = SessionMode.Tn3270;
                    logger.LogInformation("Negotiated plain TN3270 session");
                    return true;
                }

                var cmd = await ReadByteAsync(cancellationToken);
                scanned++;

                switch (cmd)
                {
                    case TelnetConstants.Do:
                    case TelnetConstants.Dont:
                    case TelnetConstants.Will:
                    case TelnetConstants.Wont:
                        {
                            var option = await ReadByteAsync(cancellationToken);
                            scanned++;
                            await HandleOptionNegotiationAsync(cmd, option, cancellationToken);
                            break;
                        }

                    case TelnetConstants.Sb:
                        {
                            var option = await ReadByteAsync(cancellationToken);
                            scanned++;
                            var data = await ReadSubnegotiationDataAsync(cancellationToken);
                            scanned += data.Length + 2; // trailing IAC SE

                            var negotiationResult = await HandleSubnegotiationAsync(
                                option,
                                data,
                                terminalType,
                                deviceName,
                                cancellationToken);

                            if (negotiationResult == NegotiationResult.Succeeded)
                            {
                                return true;
                            }

                            if (negotiationResult == NegotiationResult.Failed)
                            {
                                return false;
                            }

                            break;
                        }

                    default:
                        break;
                }
            }
        }
        catch (EndOfStreamException ex)
        {
            LogConnectionClosedDuringNegotiation(logger, ex);
        }

        logger.LogWarning("Terminal negotiation did not complete");
        return false;
    }

    public async Task SendAsync(Tn3270EFrame frame, CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        var payload = _sessionMode == SessionMode.Tn3270
            ? TelnetProtocol.EscapeIac(frame.Data.Span)
            : BuildTn3270EPayload(frame);

        // Record ends with IAC EOR in both TN3270 and TN3270E.
        var packet = new byte[payload.Length + 2];
        payload.CopyTo(packet, 0);
        packet[^2] = TelnetConstants.Iac;
        packet[^1] = TelnetConstants.Eor;

        await WriteAsync(packet, cancellationToken);
        LogSentFrame(logger, frame.DataType, frame.SequenceNumber, frame.Data.Length);
    }

    public async Task<Tn3270EFrame> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        // Read bytes until we encounter IAC EOR, collecting the payload.
        var payload = new List<byte>();
        var expectingEor = false;

        while (true)
        {
            var b = await ReadByteAsync(cancellationToken);

            if (expectingEor)
            {
                expectingEor = false;

                if (b == TelnetConstants.Eor)
                {
                    break; // end of frame
                }

                if (b == TelnetConstants.Iac)
                {
                    // IAC IAC inside data — literal 0xFF
                    payload.Add(TelnetConstants.Iac);
                    continue;
                }

                // IAC <other> — skip Telnet command bytes embedded in data
                _ = await ReadByteAsync(cancellationToken); // consume option byte if needed
                continue;
            }

            if (b == TelnetConstants.Iac)
            {
                expectingEor = true;
                continue;
            }

            payload.Add(b);
        }

        var payloadSpan = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(payload);

        if (_sessionMode == SessionMode.Tn3270)
        {
            LogReceivedFrame(logger, Tn3270EDataType.Data3270, 0, payload.Count);
            return new Tn3270EFrame(Tn3270EDataType.Data3270, 0x00, 0x00, 0, payload.ToArray());
        }

        if (!Tn3270EProtocol.TryParseHeader(
                payloadSpan,
                out var dataType,
                out var requestFlag,
                out var responseFlag,
                out var seqNum))
        {
            throw new InvalidDataException(
                $"Received TN3270E frame is too short ({payload.Count} bytes) to contain a valid header.");
        }

        var data = payloadSpan[TelnetConstants.Tn3270EHeaderSize..].ToArray();

        LogReceivedFrame(logger, dataType, seqNum, data.Length);

        return new Tn3270EFrame(dataType, requestFlag, responseFlag, seqNum, data);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_stream is null)
        {
            return;
        }

        try
        {
            await _stream.FlushAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            LogFlushError(logger, ex);
        }
        finally
        {
            await _stream.DisposeAsync();
            _stream = null;
            _connectedHost = null;
            _connectedPort = 0;
            _sessionMode = SessionMode.Unknown;
            _readBuffer.Clear();
            logger.LogDebug("Disconnected from host");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await DisconnectAsync(CancellationToken.None);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private void EnsureConnected()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_stream is null)
        {
            throw new InvalidOperationException("Not connected. Call ConnectAsync first.");
        }
    }

    private async Task WriteAsync(byte[] data, CancellationToken cancellationToken)
    {
        await _stream!.WriteAsync(data, cancellationToken);
        await _stream.FlushAsync(cancellationToken);
    }

    private async Task<byte> ReadByteAsync(CancellationToken cancellationToken)
    {
        if (_readBuffer.Count > 0)
        {
            var buffered = _readBuffer[0];
            _readBuffer.RemoveAt(0);
            return buffered;
        }

        var singleByte = new byte[1];
        var read = await _stream!.ReadAsync(singleByte, cancellationToken);
        if (read == 0)
        {
            throw new EndOfStreamException("Connection closed by remote host.");
        }

        return singleByte[0];
    }

    private static byte[] BuildTn3270EPayload(Tn3270EFrame frame)
    {
        var header = Tn3270EProtocol.BuildHeader(
            frame.DataType, frame.RequestFlag, frame.ResponseFlag, frame.SequenceNumber);
        var escapedData = TelnetProtocol.EscapeIac(frame.Data.Span);
        var payload = new byte[header.Length + escapedData.Length];
        header.CopyTo(payload, 0);
        escapedData.CopyTo(payload, header.Length);
        return payload;
    }

    private async Task HandleOptionNegotiationAsync(
        byte command,
        byte option,
        CancellationToken cancellationToken)
    {
        byte? responseCommand = command switch
        {
            TelnetConstants.Do when option is TelnetConstants.OptionBinary
                or TelnetConstants.OptionEor
                or TelnetConstants.OptionTerminalType
                or TelnetConstants.OptionTn3270E => TelnetConstants.Will,
            TelnetConstants.Will when option is TelnetConstants.OptionBinary
                or TelnetConstants.OptionEor
                or TelnetConstants.OptionTn3270E => TelnetConstants.Do,
            TelnetConstants.Do => TelnetConstants.Wont,
            TelnetConstants.Will => TelnetConstants.Dont,
            _ => null,
        };

        if (responseCommand is null)
        {
            return;
        }

        var response = TelnetProtocol.BuildOptionCommand(responseCommand.Value, option);
        await WriteAsync(response, cancellationToken);
    }

    private async Task<NegotiationResult> HandleSubnegotiationAsync(
        byte option,
        byte[] data,
        string terminalType,
        string? deviceName,
        CancellationToken cancellationToken)
    {
        if (option == TelnetConstants.OptionTerminalType
            && data.Length == 1
            && data[0] == TelnetConstants.TerminalTypeSend)
        {
            var payload = Tn3270EProtocol.BuildTerminalTypeIs(terminalType);
            var response = TelnetProtocol.BuildSubnegotiation(TelnetConstants.OptionTerminalType, payload);
            await WriteAsync(response, cancellationToken);
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("Sent TERMINAL-TYPE IS {TerminalType}", terminalType);
            }
            return NegotiationResult.Continue;
        }

        if (option != TelnetConstants.OptionTn3270E)
        {
            return NegotiationResult.Continue;
        }

        if (data.Length >= 2
            && data[0] == TelnetConstants.Tn3270EDeviceType
            && data[1] == TelnetConstants.Tn3270ESend)
        {
            var requestPayload = Tn3270EProtocol.BuildDeviceTypeRequest(terminalType, deviceName);
            var requestSb = TelnetProtocol.BuildSubnegotiation(
                TelnetConstants.OptionTn3270E, requestPayload);
            await WriteAsync(requestSb, cancellationToken);
            LogSentDeviceTypeRequest(logger, terminalType);
            return NegotiationResult.Continue;
        }

        var parsed = Tn3270EProtocol.TryParseDeviceTypeResponse(
            data,
            out var accepted,
            out var confirmedType,
            out var confirmedName,
            out var rejectCode);

        if (!parsed)
        {
            logger.LogDebug("Ignoring unrecognised TN3270E sub-negotiation");
            return NegotiationResult.Continue;
        }

        if (!accepted)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                LogNegotiationRejected(logger, Tn3270EProtocol.DescribeRejectReason(rejectCode));
            }

            return NegotiationResult.Failed;
        }

        _sessionMode = SessionMode.Tn3270E;
        LogNegotiationAccepted(logger, confirmedType, confirmedName);
        return NegotiationResult.Succeeded;
    }

    /// <summary>
    /// Reads sub-negotiation payload bytes until IAC SE, handling escaped IAC IAC pairs.
    /// Returns the raw bytes (before unescaping).
    /// </summary>
    private async Task<byte[]> ReadSubnegotiationDataAsync(CancellationToken cancellationToken)
    {
        var data = new List<byte>();

        while (true)
        {
            var b = await ReadByteAsync(cancellationToken);

            if (b != TelnetConstants.Iac)
            {
                data.Add(b);
                continue;
            }

            var next = await ReadByteAsync(cancellationToken);
            if (next == TelnetConstants.Se)
            {
                break;
            }

            // IAC IAC — literal 0xFF
            data.Add(TelnetConstants.Iac);
            if (next != TelnetConstants.Iac)
            {
                data.Add(next);
            }
        }

        return [.. data];
    }

    // -------------------------------------------------------------------------
    // Source-generated logger methods (avoids boxing and params array allocation)
    // -------------------------------------------------------------------------

    [LoggerMessage(Level = LogLevel.Debug, Message = "Opening TCP connection to {Host}:{Port}")]
    private static partial void LogOpeningConnection(ILogger logger, string host, int port);

    [LoggerMessage(Level = LogLevel.Debug, Message = "TCP connection open to {Host}:{Port}")]
    private static partial void LogConnectionOpen(ILogger logger, string host, int port);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Sent DEVICE-TYPE REQUEST {TerminalType}")]
    private static partial void LogSentDeviceTypeRequest(ILogger logger, string terminalType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Server rejected TN3270E device type: {Reason}")]
    private static partial void LogNegotiationRejected(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "TN3270E negotiation accepted: type={TerminalType} device={DeviceName}")]
    private static partial void LogNegotiationAccepted(ILogger logger, string terminalType, string deviceName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Sent TN3270E frame: DataType={DataType} Seq={Seq} Length={Len}")]
    private static partial void LogSentFrame(ILogger logger, Tn3270EDataType dataType, ushort seq, int len);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Received TN3270E frame: DataType={DataType} Seq={Seq} Length={Len}")]
    private static partial void LogReceivedFrame(ILogger logger, Tn3270EDataType dataType, ushort seq, int len);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Error flushing stream during disconnect")]
    private static partial void LogFlushError(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Server closed the connection during terminal negotiation")]
    private static partial void LogConnectionClosedDuringNegotiation(ILogger logger, Exception ex);
}
