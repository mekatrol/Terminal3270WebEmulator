namespace Terminal.Common.Protocol;

/// <summary>
/// Telnet and TN3270E byte-level constants (RFC 854, RFC 2355).
/// </summary>
internal static class TelnetConstants
{
    // Telnet commands
    internal const byte Se = 240;    // End of subnegotiation
    internal const byte Nop = 241;   // No operation
    internal const byte Sb = 250;    // Start of subnegotiation
    internal const byte Will = 251;  // I will use this option
    internal const byte Wont = 252;  // I won't use this option
    internal const byte Do = 253;    // Please use this option
    internal const byte Dont = 254;  // Please stop using this option
    internal const byte Iac = 255;   // Interpret As Command

    // Telnet options
    internal const byte OptionBinary = 0;        // RFC 856
    internal const byte OptionEor = 25;          // RFC 885 — End of Record
    internal const byte OptionTn3270E = 40;      // RFC 2355 — TN3270E
    internal const byte OptionTerminalType = 24; // RFC 1091

    // Telnet EOR command byte (follows IAC)
    internal const byte Eor = 239;

    // TN3270E sub-negotiation function codes (RFC 2355 §5)
    internal const byte Tn3270EAssociate = 0x00;
    internal const byte Tn3270EConnect = 0x01;
    internal const byte Tn3270EDeviceType = 0x02;
    internal const byte Tn3270EFunctions = 0x03;
    internal const byte Tn3270EIs = 0x04;
    internal const byte Tn3270EReason = 0x05;
    internal const byte Tn3270EReject = 0x06;
    internal const byte Tn3270ERequest = 0x07;
    internal const byte Tn3270ESend = 0x08;

    // TERMINAL-TYPE sub-negotiation bytes (RFC 1091)
    internal const byte TerminalTypeIs = 0x00;
    internal const byte TerminalTypeSend = 0x01;

    // TN3270E frame header size (DATA-TYPE + REQUEST + RESPONSE + SEQ[2])
    internal const int Tn3270EHeaderSize = 5;

    // TN3270E rejection reason codes
    internal const byte ReasonConnPartner = 0x00;
    internal const byte ReasonDeviceInUse = 0x01;
    internal const byte ReasonInvAssociate = 0x02;
    internal const byte ReasonInvName = 0x03;
    internal const byte ReasonInvDeviceType = 0x04;
    internal const byte ReasonTypeNameError = 0x05;
    internal const byte ReasonUnknownError = 0x06;
    internal const byte ReasonUnsupportedReq = 0x07;
}
