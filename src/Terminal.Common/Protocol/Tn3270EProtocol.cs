using System.Text;
using Terminal.Common.Models;

namespace Terminal.Common.Protocol;

/// <summary>
/// Pure-function helpers for building and parsing TN3270E-specific protocol structures
/// (RFC 2355). All methods are stateless and suitable for unit testing without a network.
/// </summary>
internal static class Tn3270EProtocol
{
    /// <summary>
    /// Builds the 5-byte TN3270E data-record header.
    /// </summary>
    internal static byte[] BuildHeader(
        Tn3270EDataType dataType,
        byte requestFlag,
        byte responseFlag,
        ushort sequenceNumber)
    {
        return
        [
            (byte)dataType,
            requestFlag,
            responseFlag,
            (byte)(sequenceNumber >> 8),
            (byte)(sequenceNumber & 0xFF),
        ];
    }

    /// <summary>
    /// Attempts to parse a 5-byte TN3270E header from the start of <paramref name="buffer"/>.
    /// </summary>
    /// <returns><c>true</c> if <paramref name="buffer"/> contains at least 5 bytes.</returns>
    internal static bool TryParseHeader(
        ReadOnlySpan<byte> buffer,
        out Tn3270EDataType dataType,
        out byte requestFlag,
        out byte responseFlag,
        out ushort sequenceNumber)
    {
        dataType = default;
        requestFlag = 0;
        responseFlag = 0;
        sequenceNumber = 0;

        if (buffer.Length < TelnetConstants.Tn3270EHeaderSize)
        {
            return false;
        }

        dataType = (Tn3270EDataType)buffer[0];
        requestFlag = buffer[1];
        responseFlag = buffer[2];
        sequenceNumber = (ushort)((buffer[3] << 8) | buffer[4]);
        return true;
    }

    /// <summary>
    /// Builds the SB payload for a <c>DEVICE-TYPE REQUEST &lt;type&gt; [CONNECT &lt;name&gt;]</c>
    /// sub-negotiation (the bytes that go between <c>IAC SB TN3270E</c> and <c>IAC SE</c>).
    /// </summary>
    internal static byte[] BuildDeviceTypeRequest(string terminalType, string? deviceName)
    {
        var typeBytes = Encoding.ASCII.GetBytes(terminalType);

        if (string.IsNullOrEmpty(deviceName))
        {
            var result = new byte[2 + typeBytes.Length];
            result[0] = TelnetConstants.Tn3270EDeviceType;
            result[1] = TelnetConstants.Tn3270ERequest;
            typeBytes.CopyTo(result, 2);
            return result;
        }

        var nameBytes = Encoding.ASCII.GetBytes(deviceName);
        var resultWithName = new byte[2 + typeBytes.Length + 1 + nameBytes.Length];
        resultWithName[0] = TelnetConstants.Tn3270EDeviceType;
        resultWithName[1] = TelnetConstants.Tn3270ERequest;
        typeBytes.CopyTo(resultWithName, 2);
        resultWithName[2 + typeBytes.Length] = TelnetConstants.Tn3270EConnect;
        nameBytes.CopyTo(resultWithName, 2 + typeBytes.Length + 1);
        return resultWithName;
    }

    /// <summary>
    /// Builds the TERMINAL-TYPE IS payload bytes for RFC 1091 sub-negotiation.
    /// </summary>
    internal static byte[] BuildTerminalTypeIs(string terminalType)
    {
        var typeBytes = Encoding.ASCII.GetBytes(terminalType);
        var result = new byte[1 + typeBytes.Length];
        result[0] = TelnetConstants.TerminalTypeIs;
        typeBytes.CopyTo(result, 1);
        return result;
    }

    /// <summary>
    /// Parses a <c>DEVICE-TYPE IS &lt;type&gt; CONNECT &lt;name&gt;</c> or
    /// <c>DEVICE-TYPE REJECT REASON &lt;code&gt;</c> sub-negotiation payload.
    /// </summary>
    /// <param name="data">Bytes following the TN3270E option byte in an SB sequence.</param>
    /// <param name="accepted"><c>true</c> if the server accepted the device type.</param>
    /// <param name="terminalType">The terminal type the server confirmed (on acceptance).</param>
    /// <param name="deviceName">The device name the server assigned (on acceptance).</param>
    /// <param name="rejectReasonCode">The rejection code (on rejection).</param>
    /// <returns><c>true</c> if the payload was recognised as a DEVICE-TYPE response.</returns>
    internal static bool TryParseDeviceTypeResponse(
        ReadOnlySpan<byte> data,
        out bool accepted,
        out string terminalType,
        out string deviceName,
        out byte rejectReasonCode)
    {
        accepted = false;
        terminalType = string.Empty;
        deviceName = string.Empty;
        rejectReasonCode = 0;

        if (data.Length < 2 || data[0] != TelnetConstants.Tn3270EDeviceType)
        {
            return false;
        }

        if (data[1] == TelnetConstants.Tn3270EIs) // DEVICE-TYPE IS
        {
            accepted = true;
            var remaining = data[2..];
            var connectIdx = remaining.IndexOf(TelnetConstants.Tn3270EConnect);
            if (connectIdx >= 0)
            {
                terminalType = Encoding.ASCII.GetString(remaining[..connectIdx]);
                deviceName = Encoding.ASCII.GetString(remaining[(connectIdx + 1)..]);
            }
            else
            {
                terminalType = Encoding.ASCII.GetString(remaining);
            }

            return true;
        }

        if (data[1] == TelnetConstants.Tn3270EReject && data.Length >= 4
            && data[2] == TelnetConstants.Tn3270EReason)
        {
            rejectReasonCode = data[3];
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns a human-readable description of a TN3270E rejection reason code.
    /// </summary>
    internal static string DescribeRejectReason(byte reasonCode) => reasonCode switch
    {
        TelnetConstants.ReasonConnPartner => "CONN-PARTNER",
        TelnetConstants.ReasonDeviceInUse => "DEVICE-IN-USE",
        TelnetConstants.ReasonInvAssociate => "INV-ASSOCIATE",
        TelnetConstants.ReasonInvName => "INV-NAME",
        TelnetConstants.ReasonInvDeviceType => "INV-DEVICE-TYPE",
        TelnetConstants.ReasonTypeNameError => "TYPE-NAME-ERROR",
        TelnetConstants.ReasonUnknownError => "UNKNOWN-ERROR",
        TelnetConstants.ReasonUnsupportedReq => "UNSUPPORTED-REQ",
        _ => $"UNKNOWN(0x{reasonCode:X2})",
    };
}
