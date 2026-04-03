using System.Text;

namespace Terminal.Common.Protocol;

/// <summary>
/// Pure-function helpers for building and parsing Telnet IAC byte sequences.
/// All methods are stateless and suitable for unit testing without a network.
/// </summary>
internal static class TelnetProtocol
{
    /// <summary>
    /// Builds a 3-byte Telnet option command: IAC &lt;command&gt; &lt;option&gt;.
    /// </summary>
    internal static byte[] BuildOptionCommand(byte command, byte option)
    {
        return [TelnetConstants.Iac, command, option];
    }

    /// <summary>
    /// Builds a Telnet sub-negotiation: IAC SB &lt;option&gt; &lt;data&gt; IAC SE.
    /// Any IAC byte (0xFF) inside <paramref name="data"/> is automatically doubled.
    /// </summary>
    internal static byte[] BuildSubnegotiation(byte option, ReadOnlySpan<byte> data)
    {
        var escaped = EscapeIac(data);
        var result = new byte[2 + 1 + escaped.Length + 2]; // IAC SB + option + data + IAC SE
        result[0] = TelnetConstants.Iac;
        result[1] = TelnetConstants.Sb;
        result[2] = option;
        escaped.CopyTo(result, 3);
        result[^2] = TelnetConstants.Iac;
        result[^1] = TelnetConstants.Se;
        return result;
    }

    /// <summary>
    /// Doubles every IAC byte (0xFF) in <paramref name="data"/> so it is not mistaken
    /// for a Telnet command when embedded in a data stream.
    /// </summary>
    internal static byte[] EscapeIac(ReadOnlySpan<byte> data)
    {
        var iacCount = 0;
        foreach (var b in data)
        {
            if (b == TelnetConstants.Iac)
            {
                iacCount++;
            }
        }

        if (iacCount == 0)
        {
            return data.ToArray();
        }

        var result = new byte[data.Length + iacCount];
        var dest = 0;
        foreach (var b in data)
        {
            result[dest++] = b;
            if (b == TelnetConstants.Iac)
            {
                result[dest++] = TelnetConstants.Iac;
            }
        }

        return result;
    }

    /// <summary>
    /// Replaces each IAC IAC (0xFF 0xFF) pair with a single IAC byte.
    /// </summary>
    internal static byte[] UnescapeIac(ReadOnlySpan<byte> data)
    {
        var pairCount = 0;
        for (var i = 0; i < data.Length - 1; i++)
        {
            if (data[i] == TelnetConstants.Iac && data[i + 1] == TelnetConstants.Iac)
            {
                pairCount++;
                i++; // skip over second IAC
            }
        }

        if (pairCount == 0)
        {
            return data.ToArray();
        }

        var result = new byte[data.Length - pairCount];
        var dest = 0;
        for (var i = 0; i < data.Length; i++)
        {
            result[dest++] = data[i];
            if (data[i] == TelnetConstants.Iac && (i + 1) < data.Length && data[i + 1] == TelnetConstants.Iac)
            {
                i++; // consume the duplicate IAC
            }
        }

        return result;
    }

    /// <summary>
    /// Attempts to parse one Telnet command from the start of <paramref name="buffer"/>.
    /// </summary>
    /// <param name="buffer">Bytes beginning with IAC (0xFF).</param>
    /// <param name="command">The parsed command byte (WILL/WONT/DO/DONT/SB…).</param>
    /// <param name="option">The option byte, or 0 for commands without one.</param>
    /// <param name="subnegotiationData">
    /// For SB commands, the raw bytes between SB and IAC SE (before IAC unescaping);
    /// otherwise empty.
    /// </param>
    /// <param name="bytesConsumed">Total bytes consumed including the leading IAC.</param>
    /// <returns><c>true</c> if a complete command was parsed; <c>false</c> if more bytes are needed.</returns>
    internal static bool TryParseCommand(
        ReadOnlySpan<byte> buffer,
        out byte command,
        out byte option,
        out ReadOnlyMemory<byte> subnegotiationData,
        out int bytesConsumed)
    {
        command = 0;
        option = 0;
        subnegotiationData = ReadOnlyMemory<byte>.Empty;
        bytesConsumed = 0;

        if (buffer.Length < 2 || buffer[0] != TelnetConstants.Iac)
        {
            return false;
        }

        command = buffer[1];

        // 2-byte commands: IAC NOP, IAC EOR, IAC SE, IAC AO, etc.
        if (command == TelnetConstants.Nop || command == TelnetConstants.Eor || command == TelnetConstants.Se)
        {
            bytesConsumed = 2;
            return true;
        }

        // 3-byte commands: IAC WILL/WONT/DO/DONT <option>
        if (command is TelnetConstants.Will or TelnetConstants.Wont
                or TelnetConstants.Do or TelnetConstants.Dont)
        {
            if (buffer.Length < 3)
            {
                return false;
            }

            option = buffer[2];
            bytesConsumed = 3;
            return true;
        }

        // Sub-negotiation: IAC SB <option> <data> IAC SE
        if (command == TelnetConstants.Sb)
        {
            if (buffer.Length < 4)
            {
                return false;
            }

            option = buffer[2];

            // Find the closing IAC SE
            for (var i = 3; i < buffer.Length - 1; i++)
            {
                if (buffer[i] == TelnetConstants.Iac)
                {
                    if (buffer[i + 1] == TelnetConstants.Se)
                    {
                        var dataBytes = buffer[3..i].ToArray();
                        subnegotiationData = dataBytes;
                        bytesConsumed = i + 2; // include IAC SE
                        return true;
                    }

                    // IAC IAC inside SB data — skip the doubled byte
                    if (buffer[i + 1] == TelnetConstants.Iac)
                    {
                        i++;
                    }
                }
            }

            return false; // incomplete
        }

        // IAC IAC — escaped literal 0xFF in the data stream, not a command
        if (command == TelnetConstants.Iac)
        {
            bytesConsumed = 2;
            return true;
        }

        // Unknown 2-byte command — consume and move on
        bytesConsumed = 2;
        return true;
    }

    /// <summary>
    /// Encodes an ASCII string as bytes suitable for use in a Telnet sub-negotiation.
    /// </summary>
    internal static byte[] EncodeAscii(string value) => Encoding.ASCII.GetBytes(value);
}
