using Microsoft.Extensions.Logging;
using Terminal.Common.Models;
using Terminal.Common.Protocol;

namespace Terminal.Common.Services.Implementation;

/// <summary>
/// Implements the TELNET/TN3270/TN3270E session logic used by the client side of the emulator.
/// </summary>
/// <remarks>
/// <para>
/// This type sits at the boundary between a raw TCP stream and higher-level 3270 frames. Its job is
/// not just to send and receive bytes, but to interpret the TELNET control channel that is interleaved
/// with application data on the same TCP connection.
/// </para>
/// <para>
/// The implementation follows the classic TELNET model from RFC 854, where command bytes are introduced
/// by IAC (Interpret As Command) and option negotiation is performed with DO/DON'T/WILL/WON'T:
/// https://www.rfc-editor.org/rfc/rfc854.html
/// </para>
/// <para>
/// For traditional TN3270 sessions, the code relies on TERMINAL-TYPE negotiation (RFC 1091),
/// BINARY transmission (RFC 856), and END-OF-RECORD framing (RFC 885):
/// https://www.rfc-editor.org/rfc/rfc1091.html
/// https://www.rfc-editor.org/rfc/rfc856.html
/// https://www.rfc-editor.org/rfc/rfc885.html
/// </para>
/// <para>
/// For TN3270E sessions, the code additionally follows RFC 2355. That RFC defines how the client and
/// server negotiate the TN3270E option, request a specific device type or device name, and then exchange
/// framed records that begin with a five-byte TN3270E header:
/// https://www.rfc-editor.org/rfc/rfc2355.html
/// </para>
/// <para>
/// One design constraint that explains several implementation details in this file is that TELNET commands
/// and application data share the same octet stream. Because of that, readers often need to look ahead to
/// decide whether a byte starts a TELNET command or is ordinary 3270 payload, and then "push back" bytes
/// that belong to the next higher-level read operation.
/// </para>
/// </remarks>
internal sealed partial class Tn3270EService(
    INetworkConnectionFactory connectionFactory,
    ILogger<Tn3270EService> logger) : ITn3270EService, IAsyncDisposable
{
    /// <summary>
    /// TELNET command bytes used while negotiating the session.
    /// </summary>
    private enum TelnetCommand : byte
    {
        /// <summary>
        /// End of subnegotiation parameters.
        /// </summary>
        /// <remarks>
        /// TELNET wraps option-specific payloads inside <c>IAC SB ... IAC SE</c>. This terminator matters
        /// because the parser cannot otherwise know where a sub-negotiation payload ends and ordinary TELNET
        /// data resumes. Reference: RFC 854, https://www.rfc-editor.org/rfc/rfc854.html
        /// </remarks>
        Se = TelnetConstants.Se,

        /// <summary>
        /// No operation.
        /// </summary>
        /// <remarks>
        /// NOP is a TELNET command that carries no option state change. The service does not actively use it,
        /// but it is listed here so command decoding remains readable when inspecting the stream. Reference:
        /// RFC 854, https://www.rfc-editor.org/rfc/rfc854.html
        /// </remarks>
        Nop = TelnetConstants.Nop,

        /// <summary>
        /// End of record.
        /// </summary>
        /// <remarks>
        /// TN3270 is record-oriented rather than newline-oriented. EOR is the TELNET signal that marks the
        /// boundary between one complete 3270 record and the next. Without it, the receiver would have no
        /// transport-level delimiter for 3270 records. Reference: RFC 885,
        /// https://www.rfc-editor.org/rfc/rfc885.html
        /// </remarks>
        Eor = TelnetConstants.Eor,

        /// <summary>
        /// Start of subnegotiation parameters.
        /// </summary>
        /// <remarks>
        /// SB starts a variable-length payload whose interpretation depends on the TELNET option that follows.
        /// TN3270E uses SB for DEVICE-TYPE and FUNCTIONS negotiation; TERMINAL-TYPE uses it to exchange the
        /// terminal model string. References:
        /// https://www.rfc-editor.org/rfc/rfc854.html
        /// https://www.rfc-editor.org/rfc/rfc1091.html
        /// https://www.rfc-editor.org/rfc/rfc2355.html
        /// </remarks>
        Sb = TelnetConstants.Sb,

        /// <summary>
        /// Sender will begin using the option.
        /// </summary>
        /// <remarks>
        /// WILL is half of TELNET's capability negotiation. It tells the peer "I am willing to perform this
        /// option on my side of the connection". The distinction matters because TELNET options are negotiated
        /// independently in each direction. Reference: RFC 854,
        /// https://www.rfc-editor.org/rfc/rfc854.html
        /// </remarks>
        Will = TelnetConstants.Will,

        /// <summary>
        /// Sender will not use the option.
        /// </summary>
        /// <remarks>
        /// WONT is the negative form of WILL. Explicit rejection is important in TELNET because silent ignore
        /// leaves the peer uncertain whether an option is pending, unsupported, or simply delayed. Reference:
        /// RFC 854, https://www.rfc-editor.org/rfc/rfc854.html
        /// </remarks>
        Wont = TelnetConstants.Wont,

        /// <summary>
        /// Request that the peer begin using the option.
        /// </summary>
        /// <remarks>
        /// DO asks the peer to enable an option for its outbound direction. TN3270 clients rely on DO/WILL for
        /// capabilities such as BINARY, EOR, and TN3270E before any framed 3270 traffic can be interpreted
        /// safely. References:
        /// https://www.rfc-editor.org/rfc/rfc854.html
        /// https://www.rfc-editor.org/rfc/rfc856.html
        /// https://www.rfc-editor.org/rfc/rfc885.html
        /// </remarks>
        Do = TelnetConstants.Do,

        /// <summary>
        /// Request that the peer stop using the option.
        /// </summary>
        /// <remarks>
        /// DONT requests that the peer disable an option. This matters because TELNET negotiation is stateful;
        /// without an explicit negative command, both sides can end up making different assumptions about
        /// whether an option is active. Reference: RFC 854,
        /// https://www.rfc-editor.org/rfc/rfc854.html
        /// </remarks>
        Dont = TelnetConstants.Dont,

        /// <summary>
        /// Interpret As Command introducer.
        /// </summary>
        /// <remarks>
        /// IAC is the byte that tells the receiver "the following byte is TELNET control information, not
        /// application payload". This is why literal 0xFF bytes must be escaped as IAC IAC inside data.
        /// References:
        /// https://www.rfc-editor.org/rfc/rfc854.html
        /// https://www.rfc-editor.org/rfc/rfc856.html
        /// </remarks>
        Iac = TelnetConstants.Iac,
    }

    /// <summary>
    /// TELNET option bytes relevant to TN3270/TN3270E negotiation.
    /// </summary>
    private enum TelnetOption : byte
    {
        /// <summary>
        /// Binary transmission option.
        /// </summary>
        /// <remarks>
        /// TN3270 traffic contains arbitrary 8-bit data, so BINARY mode is necessary to prevent TELNET's
        /// default NVT text assumptions from corrupting the 3270 data stream. Reference: RFC 856,
        /// https://www.rfc-editor.org/rfc/rfc856.html
        /// </remarks>
        Binary = TelnetConstants.OptionBinary,

        /// <summary>
        /// TERMINAL-TYPE option.
        /// </summary>
        /// <remarks>
        /// The host asks for the client terminal model so it can tailor the 3270 data stream to the device
        /// capabilities being emulated. TN3270/TN3270E sessions use this to identify models such as IBM-3278-2-E.
        /// Reference: RFC 1091, https://www.rfc-editor.org/rfc/rfc1091.html
        /// </remarks>
        TerminalType = TelnetConstants.OptionTerminalType,

        /// <summary>
        /// End Of Record option.
        /// </summary>
        /// <remarks>
        /// EOR makes TELNET suitable for record-oriented protocols by providing an explicit end marker. That is
        /// essential for 3270, where the application cares about complete records rather than a raw byte stream.
        /// Reference: RFC 885, https://www.rfc-editor.org/rfc/rfc885.html
        /// </remarks>
        Eor = TelnetConstants.OptionEor,

        /// <summary>
        /// TN3270E option.
        /// </summary>
        /// <remarks>
        /// TN3270E extends traditional TN3270 with typed records, request/response correlation, and richer
        /// session negotiation. The option must be explicitly negotiated before the five-byte TN3270E header is
        /// valid on the wire. Reference: RFC 2355, https://www.rfc-editor.org/rfc/rfc2355.html
        /// </remarks>
        Tn3270E = TelnetConstants.OptionTn3270E,
    }

    /// <summary>
    /// Current protocol mode selected for the active session.
    /// </summary>
    private enum SessionMode
    {
        /// <summary>
        /// Negotiation has not yet determined the active mode.
        /// </summary>
        /// <remarks>
        /// The service starts here after connecting because it cannot interpret inbound frames correctly until
        /// it knows whether the peer accepted TN3270E or fell back to classic TN3270 framing.
        /// </remarks>
        Unknown,

        /// <summary>
        /// Plain TN3270 mode without TN3270E framing.
        /// </summary>
        /// <remarks>
        /// In this mode records are transmitted as escaped 3270 payload followed by IAC EOR, with no TN3270E
        /// header. This is the fallback behaviour when TN3270E is not negotiated.
        /// </remarks>
        Tn3270,

        /// <summary>
        /// TN3270E mode with framed records and headers.
        /// </summary>
        /// <remarks>
        /// In this mode every record starts with the TN3270E five-byte header defined by RFC 2355 section 8.1,
        /// followed by TELNET-escaped payload bytes and terminated by IAC EOR.
        /// </remarks>
        Tn3270E,
    }

    /// <summary>
    /// Outcome of handling one negotiation step.
    /// </summary>
    private enum NegotiationResult
    {
        /// <summary>
        /// Negotiation should continue.
        /// </summary>
        /// <remarks>
        /// Used when a negotiation step consumed valid TELNET traffic but did not yet establish the final
        /// session mode, so the caller should keep reading more negotiation bytes.
        /// </remarks>
        Continue,

        /// <summary>
        /// Negotiation completed successfully.
        /// </summary>
        /// <remarks>
        /// Used when the service has enough confirmed state to begin exchanging 3270 application records.
        /// </remarks>
        Succeeded,

        /// <summary>
        /// Negotiation failed and should stop.
        /// </summary>
        /// <remarks>
        /// Used when the peer explicitly rejects the requested TN3270E parameters and the caller should stop
        /// treating the handshake as successful.
        /// </remarks>
        Failed,
    }

    // The active TCP stream. Once connected, all TELNET commands, sub-negotiations, and 3270 records travel
    // through this single full-duplex byte stream as described by RFC 854.
    private Stream? _stream;

    // Stored for diagnostics and status reporting so callers can inspect where the current session is bound.
    private string? _connectedHost;

    // Stored alongside the host for the same reason; logging both values makes transport-level issues easier
    // to diagnose when multiple mainframe endpoints exist.
    private int _connectedPort;

    // Guards against use-after-dispose, which is especially important for network services because reused
    // instances could otherwise write to a closed stream or expose stale connection state.
    private bool _disposed;

    // Records which frame format the service must use after negotiation. The send/receive paths branch on
    // this so they can interpret payloads without repeatedly re-inferring session state.
    private SessionMode _sessionMode;

    // Pushback buffer for bytes that were already read from the wire in order to make a protocol decision,
    // but actually belong to the next consumer. This is needed because TELNET embeds control information
    // inline with data (RFC 854), so the code sometimes has to read one byte ahead to determine whether
    // the session is still negotiating or has already started sending 3270 data.
    private readonly List<byte> _readBuffer = [];

    /// <summary>
    /// Gets a value indicating whether a network stream is currently associated with this service.
    /// </summary>
    /// <remarks>
    /// This is intentionally a transport-level indicator, not a protocol-level one. A value of
    /// <see langword="true"/> means a TCP connection object exists; it does not guarantee TN3270/TN3270E
    /// negotiation has completed.
    /// </remarks>
    public bool IsConnected => _stream is not null;

    /// <summary>
    /// Gets the host name used for the current connection, if connected.
    /// </summary>
    /// <remarks>
    /// This is retained for diagnostics and observability so logs and callers can correlate protocol events with
    /// the configured remote endpoint.
    /// </remarks>
    public string? ConnectedHost => _connectedHost;

    /// <summary>
    /// Gets the remote TCP port used for the current connection, if connected.
    /// </summary>
    /// <remarks>
    /// Tracking the port alongside the host avoids ambiguity when the same mainframe environment exposes
    /// multiple TN3270 listeners for different purposes.
    /// </remarks>
    public int ConnectedPort => _connectedPort;

    /// <summary>
    /// Opens the underlying TCP connection to the remote TELNET/TN3270 server.
    /// </summary>
    /// <param name="host">Remote host name or IP address.</param>
    /// <param name="port">Remote TCP port.</param>
    /// <param name="cancellationToken">Token used to cancel the asynchronous connect operation.</param>
    /// <remarks>
    /// <para>
    /// This method only establishes the transport. It does not perform TELNET option negotiation or TN3270E
    /// capability exchange; that is deferred to <see cref="NegotiateAsync(string, string?, CancellationToken)"/>
    /// so connection establishment and protocol selection remain explicit steps.
    /// </para>
    /// <para>
    /// Separating connect from negotiate is useful because TELNET is layered on top of TCP rather than embedded
    /// in the socket open itself. Reference: RFC 854, https://www.rfc-editor.org/rfc/rfc854.html
    /// </para>
    /// </remarks>
    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_stream is not null)
        {
            throw new InvalidOperationException("Already connected. Call DisconnectAsync first.");
        }

        LogOpeningConnection(logger, host, port);

        // Delegate socket creation to the injected factory so transport policy such as TLS, proxying, test
        // doubles, or platform-specific socket settings can be supplied outside this protocol service.
        _stream = await connectionFactory.ConnectAsync(host, port, cancellationToken);
        _connectedHost = host;
        _connectedPort = port;

        // Start in Unknown mode because the stream exists, but the service still does not know whether the
        // peer will accept TN3270E or fall back to classic TN3270 framing.
        _sessionMode = SessionMode.Unknown;

        LogConnectionOpen(logger, host, port);
    }

    /// <summary>
    /// Performs TELNET and TN3270E negotiation until the session is ready for application records.
    /// </summary>
    /// <param name="terminalType">Terminal model to advertise to the host.</param>
    /// <param name="deviceName">Optional device/resource name requested during TN3270E DEVICE-TYPE negotiation.</param>
    /// <param name="cancellationToken">Token used to cancel the asynchronous negotiation loop.</param>
    /// <returns>
    /// <see langword="true"/> when the service has established either TN3270 or TN3270E mode; otherwise,
    /// <see langword="false"/> when negotiation fails or ends prematurely.
    /// </returns>
    /// <remarks>
    /// <para>
    /// TELNET negotiation is a byte-by-byte conversation rather than a single handshake packet. The service
    /// therefore reads incrementally from the stream, responding to option commands and sub-negotiation payloads
    /// as they arrive.
    /// </para>
    /// <para>
    /// The method accepts both successful TN3270E negotiation and fallback to classic TN3270. That matches real
    /// host behaviour: some servers offer TN3270E and some do not, yet both still carry valid 3270 sessions.
    /// References:
    /// https://www.rfc-editor.org/rfc/rfc854.html
    /// https://www.rfc-editor.org/rfc/rfc2355.html
    /// </para>
    /// </remarks>
    public async Task<bool> NegotiateAsync(
        string terminalType,
        string? deviceName = null,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        // Prevent an endless read loop if the server behaves unexpectedly. Negotiation traffic is normally
        // short, so crossing this threshold strongly suggests a peer problem or a parser mismatch.
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
                    // We expected TELNET negotiation traffic, but the peer sent an ordinary data byte instead.
                    // That is the practical signal that the server has fallen back to traditional TN3270
                    // behaviour and has started the first 3270 record without using TN3270E sub-negotiation.
                    //
                    // We must not lose that byte, because it is part of the first application record rather
                    // than a negotiation artifact. Insert it back at the front of the pushback buffer so that
                    // the subsequent ReceiveAsync call sees the stream exactly as if NegotiateAsync had never
                    // consumed it.
                    //
                    // Why this matters:
                    // - RFC 854 defines TELNET as a single byte stream containing both commands and data.
                    // - RFC 2355 explicitly allows implementations to use traditional tn3270 when TN3270E
                    //   is not successfully negotiated.
                    // - RFC 885 defines IAC EOR framing for record boundaries, so dropping the first data
                    //   byte here would corrupt the first 3270 record delivered to the emulator.
                    //
                    // References:
                    // RFC 854: https://www.rfc-editor.org/rfc/rfc854.html
                    // RFC 885: https://www.rfc-editor.org/rfc/rfc885.html
                    // RFC 2355 section 2 and section 13.4 examples:
                    // https://www.rfc-editor.org/rfc/rfc2355.html
                    _readBuffer.Insert(0, b);
                    _sessionMode = SessionMode.Tn3270;
                    logger.LogInformation("Negotiated plain TN3270 session");
                    return true;
                }

                var cmd = (TelnetCommand)await ReadByteAsync(cancellationToken);
                scanned++;

                // TELNET negotiation has a small set of control verbs that matter for capability exchange.
                // Everything else is either irrelevant here or intentionally ignored to keep the client from
                // taking action on unsupported command types.
                switch (cmd)
                {
                    case TelnetCommand.Do:
                    case TelnetCommand.Dont:
                    case TelnetCommand.Will:
                    case TelnetCommand.Wont:
                        // Basic TELNET option negotiation. This is where we confirm whether the server wants us
                        // to use BINARY, EOR, TERMINAL-TYPE, or TN3270E. RFC 854 requires the option state
                        // machine to be driven by DO/DON'T/WILL/WON'T exchanges.
                        scanned += await HandleNegotiationCommandAsync(cmd, cancellationToken);
                        break;

                    case TelnetCommand.Sb:
                        // SB ... IAC SE wraps option-specific sub-negotiation payloads. For this service, those
                        // payloads are primarily TERMINAL-TYPE and TN3270E negotiation messages.
                        var (negotiationResult, consumedBytes) = await HandleSubnegotiationCommandAsync(
                            terminalType,
                            deviceName,
                            cancellationToken);
                        scanned += consumedBytes;

                        if (negotiationResult == NegotiationResult.Succeeded)
                        {
                            return true;
                        }

                        if (negotiationResult == NegotiationResult.Failed)
                        {
                            return false;
                        }

                        break;

                    default:
                        // Ignore unsupported TELNET commands during negotiation. This is safer than failing the
                        // entire session on control bytes that are not relevant to TN3270/TN3270E startup.
                        break;
                }
            }
        }
        catch (EndOfStreamException ex)
        {
            // A half-open negotiation cannot produce a usable terminal session, so log the transport failure
            // and let the caller see the unsuccessful result.
            LogConnectionClosedDuringNegotiation(logger, ex);
        }

        logger.LogWarning("Terminal negotiation did not complete");
        return false;
    }

    /// <summary>
    /// Reads the TELNET option byte that follows a DO/DON'T/WILL/WON'T command and applies the reply logic.
    /// </summary>
    /// <param name="command">The previously-read TELNET negotiation verb.</param>
    /// <param name="cancellationToken">Token used to cancel asynchronous reads and writes.</param>
    /// <returns>The number of stream bytes consumed after the command byte.</returns>
    /// <remarks>
    /// Splitting this out keeps the main negotiation loop readable while preserving exact byte accounting. The
    /// return value is always one because a TELNET option negotiation command carries exactly one option byte
    /// after the verb. Reference: RFC 854, https://www.rfc-editor.org/rfc/rfc854.html
    /// </remarks>
    private async Task<int> HandleNegotiationCommandAsync(
        TelnetCommand command,
        CancellationToken cancellationToken)
    {
        var option = (TelnetOption)await ReadByteAsync(cancellationToken);
        await HandleOptionNegotiationAsync(command, option, cancellationToken);
        return 1;
    }

    /// <summary>
    /// Reads one <c>SB ... IAC SE</c> block and dispatches it to the option-specific sub-negotiation handler.
    /// </summary>
    /// <param name="terminalType">Terminal model to advertise when the host requests it.</param>
    /// <param name="deviceName">Optional TN3270E device/resource name.</param>
    /// <param name="cancellationToken">Token used to cancel asynchronous reads and writes.</param>
    /// <returns>
    /// A tuple containing the negotiation outcome and the number of bytes consumed after the leading
    /// <c>IAC SB</c> sequence: one option byte, the payload bytes, and the trailing <c>IAC SE</c>.
    /// </returns>
    /// <remarks>
    /// Pulling this logic into a dedicated helper keeps the main negotiation loop focused on the TELNET state
    /// machine while still making the byte-accounting explicit. The consumed-byte count feeds the loop's guard
    /// against unbounded negotiation traffic.
    /// </remarks>
    private async Task<(NegotiationResult Result, int ConsumedBytes)> HandleSubnegotiationCommandAsync(
        string terminalType,
        string? deviceName,
        CancellationToken cancellationToken)
    {
        var option = (TelnetOption)await ReadByteAsync(cancellationToken);
        var data = await ReadSubnegotiationDataAsync(cancellationToken);
        var result = await HandleSubnegotiationAsync(
            option,
            data,
            terminalType,
            deviceName,
            cancellationToken);
        return (result, data.Length + 3);
    }

    /// <summary>
    /// Sends one outbound TN3270 or TN3270E record.
    /// </summary>
    /// <param name="frame">Frame metadata and payload to transmit.</param>
    /// <param name="cancellationToken">Token used to cancel the asynchronous write.</param>
    /// <remarks>
    /// <para>
    /// The same method serves both session modes because the outer transport is still TELNET in both cases.
    /// The difference is whether the payload begins directly with 3270 data or with the TN3270E header defined
    /// by RFC 2355.
    /// </para>
    /// <para>
    /// In either mode the completed record is terminated with <c>IAC EOR</c> so the peer can recover record
    /// boundaries from the TELNET stream. References:
    /// https://www.rfc-editor.org/rfc/rfc885.html
    /// https://www.rfc-editor.org/rfc/rfc2355.html
    /// </para>
    /// </remarks>
    public async Task SendAsync(Tn3270EFrame frame, CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        byte[] payload;
        if (_sessionMode == SessionMode.Tn3270)
        {
            // Traditional TN3270 has no TN3270E header. The payload is just a 3270 data record, with
            // literal 0xFF escaped as IAC IAC per RFC 854/RFC 856 so TELNET does not misread data bytes
            // as command introducers.
            payload = TelnetProtocol.EscapeIac(frame.Data.Span);
        }
        else
        {
            // TN3270E adds a 5-byte header before the escaped payload so the peer can distinguish DATA,
            // RESPONSE, REQUEST, SCS-DATA, BIND-IMAGE, and other record types defined by RFC 2355 section 8.
            payload = BuildTn3270EPayload(frame);
        }

        // Allocate a contiguous packet so the write is emitted as one logical TELNET record containing payload
        // bytes followed by the required IAC EOR terminator.
        //
        // Both traditional TN3270 and TN3270E send records as TELNET data units terminated by IAC EOR.
        // EOR is what tells the peer where one complete 3270 record ends and the next begins.
        // Reference: RFC 885 section 5/6, https://www.rfc-editor.org/rfc/rfc885.html
        var packet = new byte[payload.Length + 2];
        payload.CopyTo(packet, 0);
        packet[^2] = TelnetConstants.Iac;
        packet[^1] = TelnetConstants.Eor;

        await WriteAsync(packet, cancellationToken);
        LogSentFrame(logger, frame.DataType, frame.SequenceNumber, frame.Data.Length);
    }

    /// <summary>
    /// Receives one inbound TN3270 or TN3270E record from the TELNET stream.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the asynchronous read.</param>
    /// <returns>The parsed frame, including TN3270E header fields when applicable.</returns>
    /// <remarks>
    /// <para>
    /// The method is record-oriented even though the underlying transport is byte-oriented. It therefore buffers
    /// bytes until the TELNET EOR marker is encountered, then interprets the completed record according to the
    /// current session mode.
    /// </para>
    /// <para>
    /// In TN3270 mode the entire payload is application data. In TN3270E mode the first five bytes are the
    /// protocol header and the remainder is the 3270/SCS/NVT payload defined by the header's data type.
    /// References:
    /// https://www.rfc-editor.org/rfc/rfc885.html
    /// https://www.rfc-editor.org/rfc/rfc2355.html
    /// </para>
    /// </remarks>
    public async Task<Tn3270EFrame> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        // Read until IAC EOR because RFC 885 defines that two-octet sequence as the record delimiter.
        // TN3270/TN3270E are record-oriented protocols layered over TELNET, so we do not expose a frame
        // upward until we have consumed one whole EOR-terminated record.
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
                    // RFC 854/RFC 856 reserve IAC as the TELNET command introducer, so a literal 0xFF byte
                    // in the application payload must be encoded on the wire as IAC IAC. Collapse the escape
                    // pair back to a single payload byte before handing the record to upper layers.
                    payload.Add(TelnetConstants.Iac);
                    continue;
                }

                // IAC <other> is TELNET control traffic, not 3270 data. Consume the rest of the command and
                // keep scanning for the real record terminator. This prevents asynchronous TELNET signalling
                // from being misinterpreted as application payload.
                _ = await ReadByteAsync(cancellationToken); // consume option byte if needed
                continue;
            }

            if (b == TelnetConstants.Iac)
            {
                // Defer the decision until the next byte because IAC can mean one of three things here:
                // 1. IAC EOR: end of record
                // 2. IAC IAC: escaped literal 0xFF in the payload
                // 3. IAC <command>: out-of-band TELNET control traffic
                expectingEor = true;
                continue;
            }

            payload.Add(b);
        }

        var payloadSpan = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(payload);

        if (_sessionMode == SessionMode.Tn3270)
        {
            // Classic TN3270 has no per-record transport header, so the whole payload is exposed as 3270 data.
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
            // A TN3270E session must deliver at least the five-byte header. If it does not, the stream is
            // malformed and upper layers should fail fast rather than misalign every subsequent frame.
            throw new InvalidDataException(
                $"Received TN3270E frame is too short ({payload.Count} bytes) to contain a valid header.");
        }

        // Slice away the transport header once parsed so consumers receive only the application payload bytes,
        // with metadata already broken out into strongly typed fields on Tn3270EFrame.
        var data = payloadSpan[TelnetConstants.Tn3270EHeaderSize..].ToArray();

        LogReceivedFrame(logger, dataType, seqNum, data.Length);

        return new Tn3270EFrame(dataType, requestFlag, responseFlag, seqNum, data);
    }

    /// <summary>
    /// Flushes and closes the active connection, resetting all cached session state.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the asynchronous flush operation.</param>
    /// <remarks>
    /// <para>
    /// Shutdown clears both transport state and parser state. That reset is important because leftover buffered
    /// bytes or a stale session mode would corrupt any future reuse of the service instance.
    /// </para>
    /// <para>
    /// Flush failures are logged rather than rethrown because the caller is already disconnecting and the most
    /// important outcome is releasing the transport cleanly.
    /// </para>
    /// </remarks>
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
            // A failed flush during disconnect is diagnostic information, not a reason to keep the socket alive.
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

            // Clear every piece of session-specific state so the instance can either be reused safely or
            // disposed without exposing details from the previous connection.
            logger.LogDebug("Disconnected from host");
        }
    }

    /// <summary>
    /// Disposes the service asynchronously by tearing down any active connection exactly once.
    /// </summary>
    /// <remarks>
    /// The implementation routes disposal through <see cref="DisconnectAsync(CancellationToken)"/> so connection
    /// shutdown behaviour stays consistent whether the caller explicitly disconnects or relies on disposal.
    /// </remarks>
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

    /// <summary>
    /// Verifies that the service is connected and not disposed before protocol work begins.
    /// </summary>
    /// <remarks>
    /// Centralising these guard checks avoids repeating subtle lifecycle rules across every public method and
    /// ensures the service fails early with a consistent message when called in the wrong state.
    /// </remarks>
    private void EnsureConnected()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_stream is null)
        {
            throw new InvalidOperationException("Not connected. Call ConnectAsync first.");
        }
    }

    /// <summary>
    /// Writes a complete TELNET record or command sequence to the network stream and flushes it immediately.
    /// </summary>
    /// <remarks>
    /// Flushing after each protocol unit favours timely terminal interaction over batching. That is the right
    /// tradeoff for interactive mainframe sessions, where visible latency is more harmful than a small amount of
    /// extra I/O overhead.
    /// </remarks>
    private async Task WriteAsync(byte[] data, CancellationToken cancellationToken)
    {
        await _stream!.WriteAsync(data, cancellationToken);
        await _stream.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Reads one byte from either the pushback buffer or the underlying network stream.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the asynchronous read.</param>
    /// <returns>The next byte in the logical TELNET stream.</returns>
    /// <remarks>
    /// A dedicated single-byte reader keeps protocol parsing simple because TELNET commands are expressed as
    /// byte sequences rather than fixed packet structures. The pushback check exists so lookahead decisions can
    /// be reversed without losing stream order.
    /// </remarks>
    private async Task<byte> ReadByteAsync(CancellationToken cancellationToken)
    {
        if (_readBuffer.Count > 0)
        {
            // Satisfy the read from the pushback buffer first. These are bytes that already came off the
            // socket but were intentionally "unread" so a later stage can observe the correct protocol
            // boundary.
            var buffered = _readBuffer[0];
            _readBuffer.RemoveAt(0);
            return buffered;
        }

        var singleByte = new byte[1];

        // Read exactly one byte because TELNET state transitions depend on byte-by-byte interpretation. Larger
        // reads would be more efficient but would also force additional buffering and parser complexity.
        var read = await _stream!.ReadAsync(singleByte, cancellationToken);
        if (read == 0)
        {
            throw new EndOfStreamException("Connection closed by remote host.");
        }

        return singleByte[0];
    }

    /// <summary>
    /// Builds the on-the-wire payload for a TN3270E frame, excluding the trailing <c>IAC EOR</c>.
    /// </summary>
    /// <param name="frame">Logical frame metadata and payload.</param>
    /// <returns>The byte sequence to place before the TELNET record terminator.</returns>
    /// <remarks>
    /// TN3270E adds a five-byte header in front of the application payload, but the enclosing transport is still
    /// TELNET. That is why the method both emits the TN3270E header and escapes literal IAC bytes in the body.
    /// References:
    /// https://www.rfc-editor.org/rfc/rfc2355.html
    /// https://www.rfc-editor.org/rfc/rfc854.html
    /// </remarks>
    private static byte[] BuildTn3270EPayload(Tn3270EFrame frame)
    {
        // TN3270E records are "header + escaped data". The header carries message classification and
        // response correlation metadata defined by RFC 2355 section 8.1; the data portion still has to
        // escape literal 0xFF bytes because the enclosing transport is still TELNET.
        var header = Tn3270EProtocol.BuildHeader(
            frame.DataType, frame.RequestFlag, frame.ResponseFlag, frame.SequenceNumber);
        var escapedData = TelnetProtocol.EscapeIac(frame.Data.Span);
        var payload = new byte[header.Length + escapedData.Length];
        header.CopyTo(payload, 0);
        escapedData.CopyTo(payload, header.Length);
        return payload;
    }

    /// <summary>
    /// Handles a single TELNET option negotiation command and emits the matching response when required.
    /// </summary>
    /// <param name="command">The inbound TELNET negotiation verb.</param>
    /// <param name="option">The TELNET option being negotiated.</param>
    /// <param name="cancellationToken">Token used to cancel asynchronous writes.</param>
    /// <remarks>
    /// TELNET option negotiation is symmetric but directional. The client has to answer each relevant DO/WILL
    /// with the complementary WILL/DO, or reject unsupported options with WONT/DONT so the peer converges on a
    /// stable capability set. Reference: RFC 854, https://www.rfc-editor.org/rfc/rfc854.html
    /// </remarks>
    private async Task HandleOptionNegotiationAsync(
        TelnetCommand command,
        TelnetOption option,
        CancellationToken cancellationToken)
    {
        // This is the TELNET permission handshake from RFC 854:
        // - DO  X  asks the peer to start performing X
        // - WILL X says "I will perform X"
        //
        // For this client we accept only the options required for TN3270/TN3270E:
        // BINARY, EOR, TERMINAL-TYPE, and TN3270E. Unsupported options are explicitly rejected so that
        // both sides converge on a well-defined capability set instead of leaving the negotiation ambiguous.
        TelnetCommand? responseCommand = command switch
        {
            TelnetCommand.Do when option is TelnetOption.Binary
                or TelnetOption.Eor
                or TelnetOption.TerminalType
                or TelnetOption.Tn3270E => TelnetCommand.Will,
            TelnetCommand.Will when option is TelnetOption.Binary
                or TelnetOption.Eor
                or TelnetOption.Tn3270E => TelnetCommand.Do,
            TelnetCommand.Do => TelnetCommand.Wont,
            TelnetCommand.Will => TelnetCommand.Dont,
            _ => null,
        };

        if (responseCommand is null)
        {
            // Some TELNET commands do not require a reply from this client, and some unsupported combinations
            // are intentionally ignored here because replying would be either redundant or misleading.
            return;
        }

        // Convert the abstract negotiation decision back into the exact three-byte TELNET command sequence
        // expected on the wire: IAC <verb> <option>.
        var response = TelnetProtocol.BuildOptionCommand((byte)responseCommand.Value, (byte)option);
        await WriteAsync(response, cancellationToken);
    }

    /// <summary>
    /// Handles TELNET sub-negotiation payloads relevant to TERMINAL-TYPE and TN3270E startup.
    /// </summary>
    /// <param name="option">The TELNET option that owns the sub-negotiation payload.</param>
    /// <param name="data">The option-specific payload bytes between <c>SB</c> and <c>SE</c>.</param>
    /// <param name="terminalType">Terminal model to advertise when requested.</param>
    /// <param name="deviceName">Optional device/resource name to request for TN3270E.</param>
    /// <param name="cancellationToken">Token used to cancel asynchronous writes.</param>
    /// <returns>The high-level effect of the sub-negotiation on the session state machine.</returns>
    /// <remarks>
    /// Sub-negotiation is where TELNET moves from "I support this option" to "here are the concrete parameters
    /// for this option". For TN3270/TN3270E, that means identifying the terminal model and optionally choosing a
    /// device/resource name before application traffic begins.
    /// </remarks>
    private async Task<NegotiationResult> HandleSubnegotiationAsync(
        TelnetOption option,
        byte[] data,
        string terminalType,
        string? deviceName,
        CancellationToken cancellationToken)
    {
        if (option == TelnetOption.TerminalType
            && data.Length == 1
            && data[0] == TelnetConstants.TerminalTypeSend)
        {
            // RFC 1091 defines TERMINAL-TYPE as an asymmetric exchange: the server asks with
            // "IAC SB TERMINAL-TYPE SEND IAC SE" and the client answers with
            // "IAC SB TERMINAL-TYPE IS <name> IAC SE".
            //
            // We send the configured terminal type here because host applications tailor their 3270
            // data stream to the declared model and capabilities of the terminal.
            var payload = Tn3270EProtocol.BuildTerminalTypeIs(terminalType);
            var response = TelnetProtocol.BuildSubnegotiation(TelnetConstants.OptionTerminalType, payload);
            await WriteAsync(response, cancellationToken);
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("Sent TERMINAL-TYPE IS {TerminalType}", terminalType);
            }

            return NegotiationResult.Continue;
        }

        if (option != TelnetOption.Tn3270E)
        {
            // Ignore unrelated sub-negotiations because this service only needs a narrow subset of TELNET
            // options to establish a 3270 session.
            return NegotiationResult.Continue;
        }

        if (data.Length >= 2
            && data[0] == TelnetConstants.Tn3270EDeviceType
            && data[1] == TelnetConstants.Tn3270ESend)
        {
            // RFC 2355 says the server initiates TN3270E device selection with
            // "IAC SB TN3270E SEND DEVICE-TYPE IAC SE". The client must answer with a
            // DEVICE-TYPE REQUEST naming the terminal model it wants to emulate and, optionally,
            // a specific resource/device name.
            var requestPayload = Tn3270EProtocol.BuildDeviceTypeRequest(terminalType, deviceName);
            var requestSb = TelnetProtocol.BuildSubnegotiation(
                TelnetConstants.OptionTn3270E,
                requestPayload);
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
            // Be tolerant of TN3270E sub-negotiation content we do not currently model. Ignoring unknown
            // payloads is safer than desynchronising the session by treating them as fatal protocol errors.
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

        // At this point the peer has accepted the DEVICE-TYPE REQUEST, so subsequent records are TN3270E
        // records with a five-byte header rather than traditional headerless TN3270 records.
        _sessionMode = SessionMode.Tn3270E;
        LogNegotiationAccepted(logger, confirmedType, confirmedName);
        return NegotiationResult.Succeeded;
    }

    /// <summary>
    /// Reads TELNET sub-negotiation payload bytes until the terminating <c>IAC SE</c> sequence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TELNET sub-negotiations are framed as <c>IAC SB &lt;option&gt; ... IAC SE</c>. The payload in the middle
    /// is option-specific, but the framing bytes are defined by RFC 854.
    /// </para>
    /// <para>
    /// The parser must also recognise <c>IAC IAC</c> as an escaped literal 0xFF appearing inside the payload.
    /// That escape rule exists because 0xFF is globally reserved as the TELNET command introducer.
    /// References:
    /// https://www.rfc-editor.org/rfc/rfc854.html
    /// https://www.rfc-editor.org/rfc/rfc856.html
    /// </para>
    /// </remarks>
    private async Task<byte[]> ReadSubnegotiationDataAsync(CancellationToken cancellationToken)
    {
        var data = new List<byte>();

        while (true)
        {
            var b = await ReadByteAsync(cancellationToken);

            if (b != TelnetConstants.Iac)
            {
                // Ordinary payload byte inside the current SB ... SE block.
                data.Add(b);
                continue;
            }

            var next = await ReadByteAsync(cancellationToken);
            if (next == TelnetConstants.Se)
            {
                // The current sub-negotiation payload is complete.
                break;
            }

            // IAC IAC is the only legal way to embed a literal 0xFF inside TELNET data. Preserve the single
            // data byte seen by higher layers rather than the doubled escape sequence seen on the wire.
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

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "TN3270E negotiation accepted: type={TerminalType} device={DeviceName}")]
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
