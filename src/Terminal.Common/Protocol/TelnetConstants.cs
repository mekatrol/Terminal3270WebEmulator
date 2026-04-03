namespace Terminal.Common.Protocol;

/// <summary>
/// Telnet and TN3270E byte-level constants (RFC 854, RFC 2355).
/// </summary>
internal static class TelnetConstants
{
    /// <summary>
    /// TELNET command: End of subnegotiation parameters.
    /// </summary>
    internal const byte Se = 240;    // End of subnegotiation

    /// <summary>
    /// TELNET command: No operation.
    /// </summary>
    internal const byte Nop = 241;   // No operation

    /// <summary>
    /// TELNET command: Start of subnegotiation parameters.
    /// </summary>
    internal const byte Sb = 250;    // Start of subnegotiation

    /// <summary>
    /// TELNET negotiation command: sender will begin using the option.
    /// </summary>
    internal const byte Will = 251;  // I will use this option

    /// <summary>
    /// TELNET negotiation command: sender refuses or stops using the option.
    /// </summary>
    internal const byte Wont = 252;  // I won't use this option

    /// <summary>
    /// TELNET negotiation command: request that the peer begin using the option.
    /// </summary>
    internal const byte Do = 253;    // Please use this option

    /// <summary>
    /// TELNET negotiation command: request that the peer stop using the option.
    /// </summary>
    internal const byte Dont = 254;  // Please stop using this option

    /// <summary>
    /// TELNET command introducer: Interpret As Command (0xFF).
    /// </summary>
    internal const byte Iac = 255;   // Interpret As Command

    /// <summary>
    /// TELNET option: binary transmission mode.
    /// </summary>
    internal const byte OptionBinary = 0;        // RFC 856

    /// <summary>
    /// TELNET option: End Of Record support.
    /// </summary>
    internal const byte OptionEor = 25;          // RFC 885 — End of Record

    /// <summary>
    /// TELNET option: TN3270E protocol support.
    /// </summary>
    internal const byte OptionTn3270E = 40;      // RFC 2355 — TN3270E

    /// <summary>
    /// TELNET option: TERMINAL-TYPE negotiation.
    /// </summary>
    internal const byte OptionTerminalType = 24; // RFC 1091

    /// <summary>
    /// TELNET command: End Of Record, sent after <see cref="Iac"/>.
    /// </summary>
    internal const byte Eor = 239;

    /// <summary>
    /// TN3270E subnegotiation verb: ASSOCIATE.
    /// </summary>
    internal const byte Tn3270EAssociate = 0x00;

    /// <summary>
    /// TN3270E subnegotiation verb: CONNECT.
    /// </summary>
    internal const byte Tn3270EConnect = 0x01;

    /// <summary>
    /// TN3270E subnegotiation verb: DEVICE-TYPE.
    /// </summary>
    internal const byte Tn3270EDeviceType = 0x02;

    /// <summary>
    /// TN3270E subnegotiation verb: FUNCTIONS.
    /// </summary>
    internal const byte Tn3270EFunctions = 0x03;

    /// <summary>
    /// TN3270E subnegotiation verb: IS.
    /// </summary>
    internal const byte Tn3270EIs = 0x04;

    /// <summary>
    /// TN3270E subnegotiation verb: REASON.
    /// </summary>
    internal const byte Tn3270EReason = 0x05;

    /// <summary>
    /// TN3270E subnegotiation verb: REJECT.
    /// </summary>
    internal const byte Tn3270EReject = 0x06;

    /// <summary>
    /// TN3270E subnegotiation verb: REQUEST.
    /// </summary>
    internal const byte Tn3270ERequest = 0x07;

    /// <summary>
    /// TN3270E subnegotiation verb: SEND.
    /// </summary>
    internal const byte Tn3270ESend = 0x08;

    /// <summary>
    /// TERMINAL-TYPE subnegotiation verb: IS, followed by the terminal type string.
    /// </summary>
    internal const byte TerminalTypeIs = 0x00;

    /// <summary>
    /// TERMINAL-TYPE subnegotiation verb: SEND, requesting the terminal type string.
    /// </summary>
    internal const byte TerminalTypeSend = 0x01;

    /// <summary>
    /// TN3270E frame header size: DATA-TYPE + REQUEST + RESPONSE + SEQ[2].
    /// </summary>
    internal const int Tn3270EHeaderSize = 5;

    /// <summary>
    /// TN3270E reject reason: connection partner error.
    /// </summary>
    internal const byte ReasonConnPartner = 0x00;

    /// <summary>
    /// TN3270E reject reason: requested device is already in use.
    /// </summary>
    internal const byte ReasonDeviceInUse = 0x01;

    /// <summary>
    /// TN3270E reject reason: invalid ASSOCIATE request.
    /// </summary>
    internal const byte ReasonInvAssociate = 0x02;

    /// <summary>
    /// TN3270E reject reason: invalid device name.
    /// </summary>
    internal const byte ReasonInvName = 0x03;

    /// <summary>
    /// TN3270E reject reason: invalid device type.
    /// </summary>
    internal const byte ReasonInvDeviceType = 0x04;

    /// <summary>
    /// TN3270E reject reason: device type name format error.
    /// </summary>
    internal const byte ReasonTypeNameError = 0x05;

    /// <summary>
    /// TN3270E reject reason: unknown error.
    /// </summary>
    internal const byte ReasonUnknownError = 0x06;

    /// <summary>
    /// TN3270E reject reason: unsupported request.
    /// </summary>
    internal const byte ReasonUnsupportedReq = 0x07;
}
