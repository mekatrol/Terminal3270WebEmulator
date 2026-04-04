using System.Text;
using Terminal.Common.Models;
using Terminal.MockServer.Screens;

namespace Terminal.MockServer.Services;

/// <summary>
/// Handles one TN3270E client session: TELNET negotiation, screen delivery, and AID-key navigation.
/// </summary>
/// <remarks>
/// This class implements the server side of the negotiation that <c>Tn3270EService</c> drives on
/// the client side.  The server sends the initial option batch, parses the client's replies, drives
/// the TN3270E DEVICE-TYPE sub-negotiation, then enters the data phase where it delivers screens and
/// reacts to AID keys sent by the client.
/// </remarks>
internal sealed partial class MockSessionHandler(
    Stream stream,
    ScreenRegistry registry,
    MockServerOptions options,
    ILogger<MockSessionHandler> logger)
{
    // -------------------------------------------------------------------------
    // Session state
    // -------------------------------------------------------------------------

    private enum SessionMode { Tn3270, Tn3270E }

    private static readonly byte[] _addressTable =
    [
        0x40, 0xC1, 0xC2, 0xC3, 0xC4, 0xC5, 0xC6, 0xC7,
        0xC8, 0xC9, 0x4A, 0x4B, 0x4C, 0x4D, 0x4E, 0x4F,
        0x50, 0xD1, 0xD2, 0xD3, 0xD4, 0xD5, 0xD6, 0xD7,
        0xD8, 0xD9, 0x5A, 0x5B, 0x5C, 0x5D, 0x5E, 0x5F,
        0x60, 0x61, 0xE2, 0xE3, 0xE4, 0xE5, 0xE6, 0xE7,
        0xE8, 0xE9, 0x6A, 0x6B, 0x6C, 0x6D, 0x6E, 0x6F,
        0xF0, 0xF1, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7,
        0xF8, 0xF9, 0x7A, 0x7B, 0x7C, 0x7D, 0x7E, 0x7F,
    ];
    private static readonly Dictionary<byte, int> _addressDecodingTable = _addressTable
        .Select(static (value, index) => new KeyValuePair<byte, int>(value, index))
        .ToDictionary(static pair => pair.Key, static pair => pair.Value);
    private static readonly Encoding _ebcdicEncoding = CreateEbcdicEncoding();
    private SessionMode _mode = SessionMode.Tn3270E;
    private ushort _sequenceNumber;

    // -------------------------------------------------------------------------
    // Public entry point
    // -------------------------------------------------------------------------

    /// <summary>
    /// Runs the full session lifecycle: negotiate, serve the initial screen, then loop on AID keys.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!await NegotiateAsync(cancellationToken))
        {
            LogNegotiationFailed(logger);
            return;
        }

        if (!registry.TryGet(options.InitialScreen, out var current) || current is null)
        {
            LogScreenNotFound(logger, options.InitialScreen);
            return;
        }

        await SendScreenAsync(current, cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            var frame = await ReceiveFrameAsync(cancellationToken);
            if (frame is null)
            {
                break; // client disconnected
            }

            if (frame.DataType != Tn3270EDataType.Data3270)
            {
                LogIgnoredFrameType(logger, frame.DataType);
                continue;
            }

            var data = frame.Data.Span;
            if (data.IsEmpty)
            {
                LogEmptyInputRecord(logger);
                continue;
            }

            var aidByte = data[0];
            var aidName = AidName(aidByte);
            LogAidReceived(logger, aidName, aidByte);
            if (logger.IsEnabled(LogLevel.Debug))
            {
                var inputDescription = DescribeInboundRecord(data);
                LogInputRecord(logger, inputDescription);
            }

            var targetId = ResolveNavigationTarget(current, aidName, data, options);

            if (targetId is null)
            {
                LogUnhandledAid(logger, aidName);
                continue;
            }

            if (targetId is "exit" or "logout")
            {
                LogExiting(logger, aidName);
                break;
            }

            if (!registry.TryGet(targetId, out var next) || next is null)
            {
                LogScreenNotFound(logger, targetId);
                continue;
            }

            current = next;
            await SendScreenAsync(current, cancellationToken);
        }
    }

    // -------------------------------------------------------------------------
    // Negotiation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Drives the server-side TELNET/TN3270E option handshake.
    /// Returns <see langword="true"/> when the session is ready for data frames.
    /// </summary>
    private async Task<bool> NegotiateAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await NegotiateInternalAsync(cancellationToken);
        }
        catch (EndOfStreamException)
        {
            LogClientDisconnectedDuringNegotiation(logger);
            return false;
        }
    }

    private async Task<bool> NegotiateInternalAsync(CancellationToken cancellationToken)
    {
        if (options.PreferPlainTn3270)
        {
            await WriteAsync(BuildPlainTn3270Announcement(), cancellationToken);
            LogPlainTn3270Requested(logger);
            return await NegotiateTn3270FallbackAsync(cancellationToken);
        }

        // Announce all options in a single burst so the client can reply to all of them
        // without waiting for a round-trip per option.
        // Announce all options in a single burst so the client can reply without a round-trip
        // per option. BINARY and EOR are required for 8-bit clean, record-oriented transport.
        // TN3270E is preferred; we fall back to classic TN3270 if the client refuses.
        byte[] initOptions =
        [
            Telnet.Iac,
            Telnet.Will,
            Telnet.OptBinary,
            Telnet.Iac,
            Telnet.Do,
            Telnet.OptBinary,
            Telnet.Iac,
            Telnet.Will,
            Telnet.OptEor,
            Telnet.Iac,
            Telnet.Do,
            Telnet.OptEor,
            Telnet.Iac,
            Telnet.Will,
            Telnet.OptTn3270E,
            Telnet.Iac,
            Telnet.Do,
            Telnet.OptTn3270E,
        ];
        await WriteAsync(initOptions, cancellationToken);

        var tn3270eAgreed = false;
        var deviceTypeSendSent = false;
        var bytesConsumed = 0;

        while (bytesConsumed < 4096)
        {
            var b = await ReadByteAsync(cancellationToken);
            bytesConsumed++;

            if (b != Telnet.Iac)
            {
                // Non-IAC byte during negotiation should not happen; ignore it.
                continue;
            }

            var cmd = await ReadByteAsync(cancellationToken);
            bytesConsumed++;

            switch (cmd)
            {
                case Telnet.Will:
                case Telnet.Wont:
                case Telnet.Do:
                case Telnet.Dont:
                    {
                        var opt = await ReadByteAsync(cancellationToken);
                        bytesConsumed++;

                        if (cmd == Telnet.Will && opt == Telnet.OptTn3270E)
                        {
                            // Client confirmed it will use TN3270E.
                            tn3270eAgreed = true;
                            LogClientWillTn3270E(logger);
                        }
                        else if (cmd == Telnet.Wont && opt == Telnet.OptTn3270E)
                        {
                            // Client refuses TN3270E — fall back to classic TN3270.
                            LogTn3270ERefused(logger);
                            return await NegotiateTn3270FallbackAsync(cancellationToken);
                        }

                        break;
                    }

                case Telnet.Sb:
                    {
                        var opt = await ReadByteAsync(cancellationToken);
                        var payload = await ReadSubnegotiationDataAsync(cancellationToken);
                        // +1 opt byte, +2 IAC SE
                        bytesConsumed += 1 + payload.Length + 2;

                        if (opt == Telnet.OptTn3270E
                            && IsDeviceTypeRequest(payload))
                        {
                            var typeSlice = ExtractDeviceTypeRequestPayload(payload);
                            var connectIdx = Array.IndexOf(typeSlice, Telnet.Tn3270eConnect);
                            var terminalType = connectIdx >= 0
                                ? Encoding.ASCII.GetString(typeSlice, 0, connectIdx)
                                : Encoding.ASCII.GetString(typeSlice);

                            LogDeviceTypeRequest(logger, terminalType);

                            var isPayload = BuildDeviceTypeIsPayload(terminalType, options.DeviceName);
                            var response = BuildSubnegotiation(Telnet.OptTn3270E, isPayload);
                            await WriteAsync(response, cancellationToken);

                            _mode = SessionMode.Tn3270E;
                            LogNegotiationComplete(logger, "TN3270E", terminalType, options.DeviceName);
                            return true;
                        }

                        break;
                    }

                default:
                    // Ignore unsupported TELNET commands during negotiation.
                    break;
            }

            // Once the client agrees to TN3270E, send the DEVICE-TYPE SEND sub-negotiation
            // exactly once to prompt the client's DEVICE-TYPE REQUEST reply.
            if (tn3270eAgreed && !deviceTypeSendSent)
            {
                deviceTypeSendSent = true;
                var sendPayload = new byte[] { Telnet.Tn3270eSend, Telnet.Tn3270eDeviceType };
                await WriteAsync(BuildSubnegotiation(Telnet.OptTn3270E, sendPayload), cancellationToken);
                LogSentDeviceTypeSend(logger);
            }
        }

        LogNegotiationExceededLimit(logger);
        return false;
    }

    /// <summary>
    /// Fallback path: negotiate classic TN3270 (no TN3270E header) via TERMINAL-TYPE exchange.
    /// </summary>
    private async Task<bool> NegotiateTn3270FallbackAsync(CancellationToken cancellationToken)
    {
        await WriteAsync(
            [Telnet.Iac, Telnet.Do, Telnet.OptTerminalType],
            cancellationToken);

        var bytesConsumed = 0;

        while (bytesConsumed < 2048)
        {
            var b = await ReadByteAsync(cancellationToken);
            bytesConsumed++;

            if (b != Telnet.Iac)
            {
                continue;
            }

            var cmd = await ReadByteAsync(cancellationToken);
            bytesConsumed++;

            switch (cmd)
            {
                case Telnet.Will:
                    {
                        var opt = await ReadByteAsync(cancellationToken);
                        bytesConsumed++;

                        if (opt == Telnet.OptTerminalType)
                        {
                            // Client will send its terminal type — ask for it.
                            var sendPayload = new byte[] { Telnet.TermTypeSend };
                            await WriteAsync(
                                BuildSubnegotiation(Telnet.OptTerminalType, sendPayload),
                                cancellationToken);
                        }

                        break;
                    }

                case Telnet.Sb:
                    {
                        var opt = await ReadByteAsync(cancellationToken);
                        var payload = await ReadSubnegotiationDataAsync(cancellationToken);
                        bytesConsumed += 1 + payload.Length + 2;

                        if (opt == Telnet.OptTerminalType
                            && payload.Length >= 1
                            && payload[0] == Telnet.TermTypeIs)
                        {
                            var terminalType = Encoding.ASCII.GetString(payload, 1, payload.Length - 1);
                            _mode = SessionMode.Tn3270;
                            LogNegotiationComplete(logger, "TN3270", terminalType, string.Empty);
                            return true;
                        }

                        break;
                    }

                case Telnet.Wont:
                case Telnet.Do:
                case Telnet.Dont:
                    await ReadByteAsync(cancellationToken); // consume option byte
                    bytesConsumed++;
                    break;
            }
        }

        LogNegotiationExceededLimit(logger);
        return false;
    }

    // -------------------------------------------------------------------------
    // Frame I/O
    // -------------------------------------------------------------------------

    private async Task SendScreenAsync(ScreenDefinition screen, CancellationToken cancellationToken)
    {
        var dataStream = DataStreamEncoder.Encode(screen);
        var frame = new Tn3270EFrame(
            Tn3270EDataType.Data3270,
            RequestFlag: 0x00,
            ResponseFlag: 0x00,
            SequenceNumber: _sequenceNumber++,
            Data: dataStream);

        await SendFrameAsync(frame, cancellationToken);
        LogScreenSent(logger, screen.Id);
    }

    private async Task SendFrameAsync(Tn3270EFrame frame, CancellationToken cancellationToken)
    {
        byte[] payload;

        if (_mode == SessionMode.Tn3270)
        {
            payload = EscapeIac(frame.Data.Span);
        }
        else
        {
            // TN3270E: 5-byte header followed by IAC-escaped payload.
            var header = new byte[]
            {
                (byte)frame.DataType,
                frame.RequestFlag,
                frame.ResponseFlag,
                (byte)(frame.SequenceNumber >> 8),
                (byte)(frame.SequenceNumber & 0xFF),
            };
            var escaped = EscapeIac(frame.Data.Span);
            payload = new byte[header.Length + escaped.Length];
            header.CopyTo(payload, 0);
            escaped.CopyTo(payload, header.Length);
        }

        // Every TN3270/TN3270E record is terminated by IAC EOR.
        var packet = new byte[payload.Length + 2];
        payload.CopyTo(packet, 0);
        packet[^2] = Telnet.Iac;
        packet[^1] = Telnet.Eor;

        await WriteAsync(packet, cancellationToken);
    }

    private async Task<Tn3270EFrame?> ReceiveFrameAsync(CancellationToken cancellationToken)
    {
        var payload = new List<byte>();
        var expectingEor = false;

        while (true)
        {
            byte b;
            try
            {
                b = await ReadByteAsync(cancellationToken);
            }
            catch (EndOfStreamException)
            {
                return null;
            }

            if (expectingEor)
            {
                expectingEor = false;

                if (b == Telnet.Eor)
                {
                    break;
                }

                if (b == Telnet.Iac)
                {
                    payload.Add(Telnet.Iac);
                    continue;
                }

                await ConsumeTelnetCommandAsync(b, cancellationToken);
                continue;
            }

            if (b == Telnet.Iac)
            {
                expectingEor = true;
                continue;
            }

            payload.Add(b);
        }

        if (_mode == SessionMode.Tn3270)
        {
            return new Tn3270EFrame(Tn3270EDataType.Data3270, 0x00, 0x00, 0, payload.ToArray());
        }

        // TN3270E: strip the 5-byte header and return the application data separately.
        if (payload.Count < 5)
        {
            return new Tn3270EFrame(Tn3270EDataType.Data3270, 0x00, 0x00, 0, Array.Empty<byte>());
        }

        var dataType = (Tn3270EDataType)payload[0];
        var reqFlag = payload[1];
        var respFlag = payload[2];
        var seqNum = (ushort)((payload[3] << 8) | payload[4]);
        var data = payload.Skip(5).ToArray();

        return new Tn3270EFrame(dataType, reqFlag, respFlag, seqNum, data);
    }

    // -------------------------------------------------------------------------
    // Low-level I/O
    // -------------------------------------------------------------------------

    private async Task<byte[]> ReadSubnegotiationDataAsync(CancellationToken cancellationToken)
    {
        var data = new List<byte>();

        while (true)
        {
            var b = await ReadByteAsync(cancellationToken);

            if (b != Telnet.Iac)
            {
                data.Add(b);
                continue;
            }

            var next = await ReadByteAsync(cancellationToken);
            if (next == Telnet.Se)
            {
                break;
            }

            // IAC IAC = escaped literal 0xFF inside a sub-negotiation payload.
            data.Add(Telnet.Iac);
        }

        return [.. data];
    }

    private async Task ConsumeTelnetCommandAsync(byte command, CancellationToken cancellationToken)
    {
        switch (command)
        {
            case Telnet.Will:
            case Telnet.Wont:
            case Telnet.Do:
            case Telnet.Dont:
                _ = await ReadByteAsync(cancellationToken);
                return;

            case Telnet.Sb:
                _ = await ReadByteAsync(cancellationToken); // option byte
                _ = await ReadSubnegotiationDataAsync(cancellationToken);
                return;

            default:
                return;
        }
    }

    private async Task<byte> ReadByteAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        var read = await stream.ReadAsync(buffer, cancellationToken);
        if (read == 0)
        {
            throw new EndOfStreamException("Client closed the connection.");
        }

        return buffer[0];
    }

    private async Task WriteAsync(byte[] data, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(data, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    // -------------------------------------------------------------------------
    // Protocol helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a TELNET sub-negotiation: IAC SB <paramref name="option"/> [payload] IAC SE.
    /// Any 0xFF bytes inside the payload are doubled as required by RFC 854.
    /// </summary>
    private static byte[] BuildSubnegotiation(byte option, byte[] payload)
    {
        var escaped = EscapeIac(payload);
        var result = new byte[3 + escaped.Length + 2];
        result[0] = Telnet.Iac;
        result[1] = Telnet.Sb;
        result[2] = option;
        escaped.CopyTo(result, 3);
        result[^2] = Telnet.Iac;
        result[^1] = Telnet.Se;
        return result;
    }

    /// <summary>
    /// Builds the TN3270E DEVICE-TYPE IS &lt;type&gt; CONNECT &lt;name&gt; payload bytes
    /// (the content between IAC SB TN3270E and IAC SE).
    /// </summary>
    private static byte[] BuildDeviceTypeIsPayload(string terminalType, string deviceName)
    {
        var typeBytes = Encoding.ASCII.GetBytes(terminalType);
        var nameBytes = Encoding.ASCII.GetBytes(deviceName);
        var result = new byte[2 + typeBytes.Length + 1 + nameBytes.Length];
        result[0] = Telnet.Tn3270eDeviceType;
        result[1] = Telnet.Tn3270eIs;
        typeBytes.CopyTo(result, 2);
        result[2 + typeBytes.Length] = Telnet.Tn3270eConnect;
        nameBytes.CopyTo(result, 2 + typeBytes.Length + 1);
        return result;
    }

    private static byte[] BuildPlainTn3270Announcement() =>
    [
        Telnet.Iac,
        Telnet.Will,
        Telnet.OptBinary,
        Telnet.Iac,
        Telnet.Do,
        Telnet.OptBinary,
        Telnet.Iac,
        Telnet.Will,
        Telnet.OptEor,
        Telnet.Iac,
        Telnet.Do,
        Telnet.OptEor,
        Telnet.Iac,
        Telnet.Do,
        Telnet.OptTerminalType,
    ];

    private static bool IsDeviceTypeRequest(byte[] payload)
    {
        return payload.Length >= 2
            && ((payload[0] == Telnet.Tn3270eDeviceType && payload[1] == Telnet.Tn3270eRequest)
                || (payload[0] == Telnet.Tn3270eRequest && payload[1] == Telnet.Tn3270eDeviceType));
    }

    private static byte[] ExtractDeviceTypeRequestPayload(byte[] payload)
    {
        return payload[0] == Telnet.Tn3270eRequest
            ? payload[2..]
            : payload[2..];
    }

    /// <summary>
    /// Doubles every 0xFF byte in <paramref name="data"/> so TELNET does not misinterpret
    /// application payload as an IAC command introducer (RFC 854 / RFC 856).
    /// </summary>
    private static byte[] EscapeIac(ReadOnlySpan<byte> data)
    {
        var extraBytes = 0;
        foreach (var b in data)
        {
            if (b == Telnet.Iac)
            {
                extraBytes++;
            }
        }

        if (extraBytes == 0)
        {
            return data.ToArray();
        }

        var result = new byte[data.Length + extraBytes];
        var j = 0;
        foreach (var b in data)
        {
            result[j++] = b;
            if (b == Telnet.Iac)
            {
                result[j++] = Telnet.Iac;
            }
        }

        return result;
    }

    // -------------------------------------------------------------------------
    // AID key mapping
    // -------------------------------------------------------------------------

    private static string AidName(byte aid) => aid switch
    {
        0x7D => "Enter",
        0x6D => "Clear",
        0x6C => "PA1",
        0x6E => "PA2",
        0x6B => "PA3",
        0xF1 => "PF1",
        0xF2 => "PF2",
        0xF3 => "PF3",
        0xF4 => "PF4",
        0xF5 => "PF5",
        0xF6 => "PF6",
        0xF7 => "PF7",
        0xF8 => "PF8",
        0xF9 => "PF9",
        0x7A => "PF10",
        0x7B => "PF11",
        0x7C => "PF12",
        0xC1 => "PF13",
        0xC2 => "PF14",
        0xC3 => "PF15",
        0xC4 => "PF16",
        0xC5 => "PF17",
        0xC6 => "PF18",
        0xC7 => "PF19",
        0xC8 => "PF20",
        0xC9 => "PF21",
        0x4A => "PF22",
        0x4B => "PF23",
        0x4C => "PF24",
        _ => $"0x{aid:X2}",
    };

    private static Encoding CreateEbcdicEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(37);
    }

    /// <summary>
    /// Resolves the next screen for the current AID and input record.
    /// The login screens are treated specially because a traditional 3270 sign-on flow does not advance
    /// solely on Enter; it advances only after the host validates the submitted credentials. The mock server
    /// mirrors that host-side decision point so the SPA can be tested against both success and failure paths.
    /// </summary>
    internal static string? ResolveNavigationTarget(
        ScreenDefinition current,
        string aidName,
        ReadOnlySpan<byte> data,
        MockServerOptions options)
    {
        if (IsLoginScreen(current.Id)
            && string.Equals(aidName, "Enter", StringComparison.OrdinalIgnoreCase))
        {
            var fieldValues = ParseFieldValues(current, data);
            var signInSucceeded = CredentialsMatch(fieldValues, options);
            return signInSucceeded
                ? current.Navigation.GetValueOrDefault(aidName)
                : "login-failed";
        }

        if (string.Equals(current.Id, "main-menu", StringComparison.OrdinalIgnoreCase)
            && string.Equals(aidName, "Enter", StringComparison.OrdinalIgnoreCase))
        {
            var fieldValues = ParseFieldValues(current, data);
            if (fieldValues.TryGetValue("option", out var option))
            {
                return option switch
                {
                    "1" => "main-menu",
                    "2" => "main-menu",
                    "3" => "login",
                    _ => "main-menu",
                };
            }
        }

        return current.Navigation.TryGetValue(aidName, out var targetId)
            ? targetId
            : null;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the screen is part of the sign-in loop.
    /// Keeping the failure variant in the same logical bucket lets the mock server re-prompt after a bad
    /// attempt without duplicating the credential-handling rules in multiple branches.
    /// </summary>
    internal static bool IsLoginScreen(string screenId) =>
        string.Equals(screenId, "login", StringComparison.OrdinalIgnoreCase)
        || string.Equals(screenId, "login-failed", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Compares submitted credentials to the configured server values.
    /// Both fields must be present and non-blank because the requested UX is to reject incomplete sign-in
    /// attempts in the same way as incorrect credentials, then redisplay the sign-in screen with an error.
    /// </summary>
    internal static bool CredentialsMatch(
        IReadOnlyDictionary<string, string> fieldValues,
        MockServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(fieldValues);
        ArgumentNullException.ThrowIfNull(options);

        if (!fieldValues.TryGetValue("username", out var submittedUserId)
            || string.IsNullOrWhiteSpace(submittedUserId))
        {
            return false;
        }

        if (!fieldValues.TryGetValue("password", out var submittedPassword)
            || string.IsNullOrWhiteSpace(submittedPassword))
        {
            return false;
        }

        return string.Equals(submittedUserId.TrimEnd(), options.SignInUserId, StringComparison.Ordinal)
            && string.Equals(submittedPassword.TrimEnd(), options.SignInPassword, StringComparison.Ordinal);
    }

    private static Dictionary<string, string> ParseFieldValues(ScreenDefinition screen, ReadOnlySpan<byte> data)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);

        if (data.Length <= 3)
        {
            return values;
        }

        var offset = 3;
        while (offset < data.Length)
        {
            if (data[offset] != 0x11 || offset + 2 >= data.Length)
            {
                break;
            }

            var address = DecodeBufferAddress(data[offset + 1], data[offset + 2]);
            offset += 3;

            var fieldStart = offset;
            while (offset < data.Length && data[offset] != 0x11)
            {
                offset++;
            }

            var fieldBytes = data[fieldStart..offset].ToArray();
            var field = screen.Fields.FirstOrDefault(candidate =>
                !string.IsNullOrWhiteSpace(candidate.Id)
                && (
                    string.Equals(candidate.Type, "input", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(candidate.Type, "input-hidden", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(candidate.Type, "input-numeric", StringComparison.OrdinalIgnoreCase))
                && BufferAddress(candidate.Row, candidate.Col, screen.Cols) == address);

            if (field is null || string.IsNullOrWhiteSpace(field.Id))
            {
                continue;
            }

            values[field.Id] = _ebcdicEncoding.GetString(fieldBytes).TrimEnd();
        }

        return values;
    }

    private static int BufferAddress(int row, int col, int cols) =>
        ((row - 1) * cols) + (col - 1);

    private static int DecodeBufferAddress(byte first, byte second)
    {
        return (_addressDecodingTable[first] << 6) | _addressDecodingTable[second];
    }

    private static string DescribeInboundRecord(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return "Empty input record";
        }

        var builder = new StringBuilder();
        builder.Append("Aid=");
        builder.Append(AidName(data[0]));
        builder.Append(" Cursor=");

        if (data.Length >= 3)
        {
            builder.Append($"0x{data[1]:X2}{data[2]:X2}");
        }
        else
        {
            builder.Append("missing");
        }

        var offset = 3;
        while (offset < data.Length)
        {
            if (data[offset] != 0x11)
            {
                builder.Append($" Raw=0x{data[offset]:X2}");
                offset++;
                continue;
            }

            if (offset + 2 >= data.Length)
            {
                builder.Append(" SBA=truncated");
                break;
            }

            builder.Append($" SBA=0x{data[offset + 1]:X2}{data[offset + 2]:X2}");
            offset += 3;

            var textStart = offset;
            while (offset < data.Length && data[offset] != 0x11)
            {
                offset++;
            }

            if (offset > textStart)
            {
                var ebcdicBytes = data[textStart..offset].ToArray();
                var decoded = DecodeEbcdic(ebcdicBytes);
                builder.Append(" Text='");
                builder.Append(decoded.TrimEnd());
                builder.Append('\'');
            }
        }

        return builder.ToString();
    }

    private static string DecodeEbcdic(byte[] bytes)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(37).GetString(bytes);
    }

    // -------------------------------------------------------------------------
    // Logger messages
    // -------------------------------------------------------------------------

    [LoggerMessage(Level = LogLevel.Warning, Message = "TN3270E negotiation failed")]
    private static partial void LogNegotiationFailed(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Client confirmed WILL TN3270E")]
    private static partial void LogClientWillTn3270E(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Client refused TN3270E — falling back to classic TN3270")]
    private static partial void LogTn3270ERefused(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Configuration requests classic TN3270 negotiation")]
    private static partial void LogPlainTn3270Requested(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Sent DEVICE-TYPE SEND")]
    private static partial void LogSentDeviceTypeSend(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Client DEVICE-TYPE REQUEST: {TerminalType}")]
    private static partial void LogDeviceTypeRequest(ILogger logger, string terminalType);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Negotiation complete: mode={Mode} terminalType={TerminalType} deviceName={DeviceName}")]
    private static partial void LogNegotiationComplete(
        ILogger logger, string mode, string terminalType, string deviceName);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Negotiation exceeded byte limit without completing")]
    private static partial void LogNegotiationExceededLimit(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Client disconnected during negotiation")]
    private static partial void LogClientDisconnectedDuringNegotiation(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Sent screen: {ScreenId}")]
    private static partial void LogScreenSent(ILogger logger, string screenId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Screen not found: {ScreenId}")]
    private static partial void LogScreenNotFound(ILogger logger, string screenId);

    [LoggerMessage(Level = LogLevel.Information, Message = "AID received: {AidName} (0x{AidByte:X2})")]
    private static partial void LogAidReceived(ILogger logger, string aidName, byte aidByte);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Input record: {Description}")]
    private static partial void LogInputRecord(ILogger logger, string description);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Ignoring non-3270 frame type: {DataType}")]
    private static partial void LogIgnoredFrameType(ILogger logger, Tn3270EDataType dataType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Received empty input record")]
    private static partial void LogEmptyInputRecord(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "No navigation target configured for AID {AidName}")]
    private static partial void LogUnhandledAid(ILogger logger, string aidName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Session ended by {AidName} navigation to 'exit'")]
    private static partial void LogExiting(ILogger logger, string aidName);
}

/// <summary>
/// TELNET and TN3270E wire-protocol constants used by <see cref="MockSessionHandler"/>.
/// Values are defined by RFC 854, RFC 856, RFC 885, RFC 1091, and RFC 2355.
/// </summary>
file static class Telnet
{
    // TELNET command bytes (RFC 854)
    internal const byte Se = 240;
    internal const byte Sb = 250;
    internal const byte Will = 251;
    internal const byte Wont = 252;
    internal const byte Do = 253;
    internal const byte Dont = 254;
    internal const byte Iac = 255;
    internal const byte Eor = 239;

    // TELNET option identifiers
    internal const byte OptBinary = 0;        // RFC 856
    internal const byte OptTerminalType = 24; // RFC 1091
    internal const byte OptEor = 25;          // RFC 885
    internal const byte OptTn3270E = 40;      // RFC 2355

    // TN3270E sub-negotiation verbs (RFC 2355 section 4)
    internal const byte Tn3270eConnect = 0x01;
    internal const byte Tn3270eDeviceType = 0x02;
    internal const byte Tn3270eIs = 0x04;
    internal const byte Tn3270eRequest = 0x07;
    internal const byte Tn3270eSend = 0x08;

    // TERMINAL-TYPE sub-negotiation verbs (RFC 1091)
    internal const byte TermTypeIs = 0x00;
    internal const byte TermTypeSend = 0x01;
}
