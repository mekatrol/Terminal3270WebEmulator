using Terminal.Common.Protocol;

namespace Terminal.Test.Unit.Protocol;

[TestClass]
public sealed class TelnetProtocolTests
{
    // -------------------------------------------------------------------------
    // BuildOptionCommand
    // -------------------------------------------------------------------------

    [TestMethod]
    public void BuildOptionCommand_ReturnsCorrectThreeBytes()
    {
        var result = TelnetProtocol.BuildOptionCommand(TelnetConstants.Do, TelnetConstants.OptionTn3270E);

        Assert.HasCount(3, result);
        Assert.AreEqual(TelnetConstants.Iac, result[0]);
        Assert.AreEqual(TelnetConstants.Do, result[1]);
        Assert.AreEqual(TelnetConstants.OptionTn3270E, result[2]);
    }

    [TestMethod]
    public void BuildOptionCommand_WillTn3270E_IsCorrect()
    {
        var result = TelnetProtocol.BuildOptionCommand(TelnetConstants.Will, TelnetConstants.OptionTn3270E);

        CollectionAssert.AreEqual(
            new byte[] { TelnetConstants.Iac, TelnetConstants.Will, TelnetConstants.OptionTn3270E },
            result);
    }

    // -------------------------------------------------------------------------
    // BuildSubnegotiation
    // -------------------------------------------------------------------------

    [TestMethod]
    public void BuildSubnegotiation_WrapsDataCorrectly()
    {
        byte[] data = [0x02, 0x08]; // DEVICE-TYPE SEND
        var result = TelnetProtocol.BuildSubnegotiation(TelnetConstants.OptionTn3270E, data);

        // IAC SB <option> <data...> IAC SE
        Assert.HasCount(7, result);
        Assert.AreEqual(TelnetConstants.Iac, result[0]);
        Assert.AreEqual(TelnetConstants.Sb, result[1]);
        Assert.AreEqual(TelnetConstants.OptionTn3270E, result[2]);
        Assert.AreEqual(0x02, result[3]);
        Assert.AreEqual(0x08, result[4]);
        Assert.AreEqual(TelnetConstants.Iac, result[5]);
        Assert.AreEqual(TelnetConstants.Se, result[6]);
    }

    [TestMethod]
    public void BuildSubnegotiation_EscapesIacByteInData()
    {
        byte[] data = [0xFF, 0x01]; // contains IAC
        var result = TelnetProtocol.BuildSubnegotiation(TelnetConstants.OptionTn3270E, data);

        // Data 0xFF becomes 0xFF 0xFF in the output
        Assert.HasCount(8, result); // 2 + 1 + 3 (0xFF doubled) + 2
        Assert.AreEqual(0xFF, result[3]);
        Assert.AreEqual(0xFF, result[4]); // doubled IAC
        Assert.AreEqual(0x01, result[5]);
    }

    // -------------------------------------------------------------------------
    // EscapeIac
    // -------------------------------------------------------------------------

    [TestMethod]
    public void EscapeIac_NoIacBytes_ReturnsSameBytes()
    {
        byte[] input = [0x01, 0x02, 0x03];
        var result = TelnetProtocol.EscapeIac(input);
        CollectionAssert.AreEqual(input, result);
    }

    [TestMethod]
    public void EscapeIac_SingleIac_IsDoubled()
    {
        byte[] input = [0x01, 0xFF, 0x02];
        var result = TelnetProtocol.EscapeIac(input);
        CollectionAssert.AreEqual(new byte[] { 0x01, 0xFF, 0xFF, 0x02 }, result);
    }

    [TestMethod]
    public void EscapeIac_MultipleIacBytes_AllDoubled()
    {
        byte[] input = [0xFF, 0xFF];
        var result = TelnetProtocol.EscapeIac(input);
        CollectionAssert.AreEqual(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, result);
    }

    [TestMethod]
    public void EscapeIac_EmptyInput_ReturnsEmptyArray()
    {
        var result = TelnetProtocol.EscapeIac([]);
        Assert.IsEmpty(result);
    }

    // -------------------------------------------------------------------------
    // UnescapeIac
    // -------------------------------------------------------------------------

    [TestMethod]
    public void UnescapeIac_NoDoubledIac_ReturnsSameBytes()
    {
        byte[] input = [0x01, 0x02, 0x03];
        var result = TelnetProtocol.UnescapeIac(input);
        CollectionAssert.AreEqual(input, result);
    }

    [TestMethod]
    public void UnescapeIac_DoubledIac_CollapsedToSingle()
    {
        byte[] input = [0x01, 0xFF, 0xFF, 0x02];
        var result = TelnetProtocol.UnescapeIac(input);
        CollectionAssert.AreEqual(new byte[] { 0x01, 0xFF, 0x02 }, result);
    }

    [TestMethod]
    public void UnescapeIac_RoundTripsWithEscape()
    {
        byte[] original = [0x01, 0xFF, 0xAB, 0xFF, 0x02];
        var escaped = TelnetProtocol.EscapeIac(original);
        var unescaped = TelnetProtocol.UnescapeIac(escaped);
        CollectionAssert.AreEqual(original, unescaped);
    }

    // -------------------------------------------------------------------------
    // TryParseCommand
    // -------------------------------------------------------------------------

    [TestMethod]
    public void TryParseCommand_IacDoTn3270E_ParsedCorrectly()
    {
        byte[] buffer = [TelnetConstants.Iac, TelnetConstants.Do, TelnetConstants.OptionTn3270E];

        var ok = TelnetProtocol.TryParseCommand(
            buffer, out var command, out var option, out _, out var consumed);

        Assert.IsTrue(ok);
        Assert.AreEqual(TelnetConstants.Do, command);
        Assert.AreEqual(TelnetConstants.OptionTn3270E, option);
        Assert.AreEqual(3, consumed);
    }

    [TestMethod]
    public void TryParseCommand_IacWill_ParsedCorrectly()
    {
        byte[] buffer = [TelnetConstants.Iac, TelnetConstants.Will, 0x18];

        var ok = TelnetProtocol.TryParseCommand(
            buffer, out var command, out var option, out _, out var consumed);

        Assert.IsTrue(ok);
        Assert.AreEqual(TelnetConstants.Will, command);
        Assert.AreEqual(0x18, option);
        Assert.AreEqual(3, consumed);
    }

    [TestMethod]
    public void TryParseCommand_TruncatedThreeByteCommand_ReturnsFalse()
    {
        byte[] buffer = [TelnetConstants.Iac, TelnetConstants.Do]; // missing option byte

        var ok = TelnetProtocol.TryParseCommand(
            buffer, out _, out _, out _, out _);

        Assert.IsFalse(ok);
    }

    [TestMethod]
    public void TryParseCommand_IacNop_ParsedAsTwoByte()
    {
        byte[] buffer = [TelnetConstants.Iac, TelnetConstants.Nop, 0x00];

        var ok = TelnetProtocol.TryParseCommand(
            buffer, out var command, out _, out _, out var consumed);

        Assert.IsTrue(ok);
        Assert.AreEqual(TelnetConstants.Nop, command);
        Assert.AreEqual(2, consumed);
    }

    [TestMethod]
    public void TryParseCommand_IacSbWithPayload_ExtractsData()
    {
        // IAC SB TN3270E 0x02 0x08 IAC SE
        byte[] buffer =
        [
            TelnetConstants.Iac,
            TelnetConstants.Sb,
            TelnetConstants.OptionTn3270E,
            0x02,
            0x08,
            TelnetConstants.Iac,
            TelnetConstants.Se,
        ];

        var ok = TelnetProtocol.TryParseCommand(
            buffer, out var command, out var option, out var sbData, out var consumed);

        Assert.IsTrue(ok);
        Assert.AreEqual(TelnetConstants.Sb, command);
        Assert.AreEqual(TelnetConstants.OptionTn3270E, option);
        Assert.AreEqual(7, consumed);
        Assert.AreEqual(2, sbData.Length);
        Assert.AreEqual(0x02, sbData.Span[0]);
        Assert.AreEqual(0x08, sbData.Span[1]);
    }

    [TestMethod]
    public void TryParseCommand_IncompleteSb_ReturnsFalse()
    {
        // IAC SB TN3270E 0x02 — missing IAC SE
        byte[] buffer = [TelnetConstants.Iac, TelnetConstants.Sb, TelnetConstants.OptionTn3270E, 0x02];

        var ok = TelnetProtocol.TryParseCommand(
            buffer, out _, out _, out _, out _);

        Assert.IsFalse(ok);
    }

    [TestMethod]
    public void TryParseCommand_NotStartingWithIac_ReturnsFalse()
    {
        byte[] buffer = [0x01, 0x02, 0x03];

        var ok = TelnetProtocol.TryParseCommand(
            buffer, out _, out _, out _, out _);

        Assert.IsFalse(ok);
    }

    // -------------------------------------------------------------------------
    // EncodeAscii
    // -------------------------------------------------------------------------

    [TestMethod]
    public void EncodeAscii_ProducesExpectedBytes()
    {
        var result = TelnetProtocol.EncodeAscii("IBM-3278-2-E");
        var expected = System.Text.Encoding.ASCII.GetBytes("IBM-3278-2-E");
        CollectionAssert.AreEqual(expected, result);
    }
}
