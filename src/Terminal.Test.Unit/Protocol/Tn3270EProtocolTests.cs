using Terminal.Common.Models;
using Terminal.Common.Protocol;

namespace Terminal.Test.Unit.Protocol;

[TestClass]
public sealed class Tn3270EProtocolTests
{
    // -------------------------------------------------------------------------
    // BuildHeader
    // -------------------------------------------------------------------------

    [TestMethod]
    public void BuildHeader_ProducesCorrectFiveBytes()
    {
        var result = Tn3270EProtocol.BuildHeader(Tn3270EDataType.Data3270, 0x00, 0x00, 0x0001);

        Assert.HasCount(5, result);
        Assert.AreEqual((byte)Tn3270EDataType.Data3270, result[0]);
        Assert.AreEqual(0x00, result[1]); // request flag
        Assert.AreEqual(0x00, result[2]); // response flag
        Assert.AreEqual(0x00, result[3]); // seq high byte
        Assert.AreEqual(0x01, result[4]); // seq low byte
    }

    [TestMethod]
    public void BuildHeader_BigEndianSequenceNumber_CorrectByteOrder()
    {
        var result = Tn3270EProtocol.BuildHeader(Tn3270EDataType.Data3270, 0x00, 0x00, 0xABCD);

        Assert.AreEqual(0xAB, result[3]);
        Assert.AreEqual(0xCD, result[4]);
    }

    // -------------------------------------------------------------------------
    // TryParseHeader
    // -------------------------------------------------------------------------

    [TestMethod]
    public void TryParseHeader_ValidBuffer_ParsesAllFields()
    {
        byte[] buffer = [(byte)Tn3270EDataType.Data3270, 0x01, 0x02, 0x00, 0x05];

        var ok = Tn3270EProtocol.TryParseHeader(
            buffer, out var dataType, out var req, out var resp, out var seq);

        Assert.IsTrue(ok);
        Assert.AreEqual(Tn3270EDataType.Data3270, dataType);
        Assert.AreEqual(0x01, req);
        Assert.AreEqual(0x02, resp);
        Assert.AreEqual((ushort)5, seq);
    }

    [TestMethod]
    public void TryParseHeader_BufferTooShort_ReturnsFalse()
    {
        byte[] buffer = [0x00, 0x00, 0x00, 0x00]; // only 4 bytes

        var ok = Tn3270EProtocol.TryParseHeader(
            buffer, out _, out _, out _, out _);

        Assert.IsFalse(ok);
    }

    [TestMethod]
    public void TryParseHeader_RoundTripsWithBuildHeader()
    {
        var built = Tn3270EProtocol.BuildHeader(Tn3270EDataType.SscpLuData, 0x01, 0x02, 0x1234);

        var ok = Tn3270EProtocol.TryParseHeader(
            built, out var dataType, out var req, out var resp, out var seq);

        Assert.IsTrue(ok);
        Assert.AreEqual(Tn3270EDataType.SscpLuData, dataType);
        Assert.AreEqual(0x01, req);
        Assert.AreEqual(0x02, resp);
        Assert.AreEqual((ushort)0x1234, seq);
    }

    // -------------------------------------------------------------------------
    // BuildDeviceTypeRequest
    // -------------------------------------------------------------------------

    [TestMethod]
    public void BuildDeviceTypeRequest_WithoutDeviceName_ProducesCorrectPayload()
    {
        var result = Tn3270EProtocol.BuildDeviceTypeRequest("IBM-3278-2-E", null);

        // Byte 0: DEVICE-TYPE (0x02), Byte 1: REQUEST (0x07), then ASCII type
        Assert.AreEqual(TelnetConstants.Tn3270EDeviceType, result[0]);
        Assert.AreEqual(TelnetConstants.Tn3270ERequest, result[1]);

        var typeBytes = System.Text.Encoding.ASCII.GetBytes("IBM-3278-2-E");
        for (var i = 0; i < typeBytes.Length; i++)
        {
            Assert.AreEqual(typeBytes[i], result[2 + i]);
        }
    }

    [TestMethod]
    public void BuildDeviceTypeRequest_WithDeviceName_IncludesConnectByte()
    {
        var result = Tn3270EProtocol.BuildDeviceTypeRequest("IBM-3278-2-E", "LU1");

        var typeBytes = System.Text.Encoding.ASCII.GetBytes("IBM-3278-2-E");
        var nameBytes = System.Text.Encoding.ASCII.GetBytes("LU1");
        var connectIdx = 2 + typeBytes.Length;

        Assert.AreEqual(TelnetConstants.Tn3270ERequest, result[1]);
        Assert.AreEqual(TelnetConstants.Tn3270EConnect, result[connectIdx]);
        for (var i = 0; i < nameBytes.Length; i++)
        {
            Assert.AreEqual(nameBytes[i], result[connectIdx + 1 + i]);
        }
    }

    [TestMethod]
    public void BuildTerminalTypeIs_ProducesCorrectPayload()
    {
        var result = Tn3270EProtocol.BuildTerminalTypeIs("IBM-3278-2-E");

        Assert.AreEqual(TelnetConstants.TerminalTypeIs, result[0]);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes("IBM-3278-2-E");
        for (var i = 0; i < typeBytes.Length; i++)
        {
            Assert.AreEqual(typeBytes[i], result[i + 1]);
        }
    }

    // -------------------------------------------------------------------------
    // TryParseDeviceTypeResponse — accepted
    // -------------------------------------------------------------------------

    [TestMethod]
    public void TryParseDeviceTypeResponse_IsResponse_ParsesTypeAndDevice()
    {
        var typeBytes = System.Text.Encoding.ASCII.GetBytes("IBM-3278-2-E");
        var nameBytes = System.Text.Encoding.ASCII.GetBytes("LU42");

        var data = new byte[2 + typeBytes.Length + 1 + nameBytes.Length];
        data[0] = TelnetConstants.Tn3270EDeviceType;
        data[1] = TelnetConstants.Tn3270EIs;
        typeBytes.CopyTo(data, 2);
        data[2 + typeBytes.Length] = TelnetConstants.Tn3270EConnect;
        nameBytes.CopyTo(data, 2 + typeBytes.Length + 1);

        var ok = Tn3270EProtocol.TryParseDeviceTypeResponse(
            data, out var accepted, out var termType, out var devName, out _);

        Assert.IsTrue(ok);
        Assert.IsTrue(accepted);
        Assert.AreEqual("IBM-3278-2-E", termType);
        Assert.AreEqual("LU42", devName);
    }

    [TestMethod]
    public void TryParseDeviceTypeResponse_IsWithoutConnect_TypeOnlyParsed()
    {
        var typeBytes = System.Text.Encoding.ASCII.GetBytes("IBM-3279-3-E");
        var data = new byte[2 + typeBytes.Length];
        data[0] = TelnetConstants.Tn3270EDeviceType;
        data[1] = TelnetConstants.Tn3270EIs;
        typeBytes.CopyTo(data, 2);

        var ok = Tn3270EProtocol.TryParseDeviceTypeResponse(
            data, out var accepted, out var termType, out _, out _);

        Assert.IsTrue(ok);
        Assert.IsTrue(accepted);
        Assert.AreEqual("IBM-3279-3-E", termType);
    }

    // -------------------------------------------------------------------------
    // TryParseDeviceTypeResponse — rejected
    // -------------------------------------------------------------------------

    [TestMethod]
    public void TryParseDeviceTypeResponse_RejectResponse_ParsesReasonCode()
    {
        byte[] data =
        [
            TelnetConstants.Tn3270EDeviceType,
            TelnetConstants.Tn3270EReject,
            TelnetConstants.Tn3270EReason,
            TelnetConstants.ReasonDeviceInUse,
        ];

        var ok = Tn3270EProtocol.TryParseDeviceTypeResponse(
            data, out var accepted, out _, out _, out var reasonCode);

        Assert.IsTrue(ok);
        Assert.IsFalse(accepted);
        Assert.AreEqual(TelnetConstants.ReasonDeviceInUse, reasonCode);
    }

    [TestMethod]
    public void TryParseDeviceTypeResponse_UnrecognisedPayload_ReturnsFalse()
    {
        byte[] data = [0x01, 0x02]; // does not start with DEVICE-TYPE

        var ok = Tn3270EProtocol.TryParseDeviceTypeResponse(
            data, out _, out _, out _, out _);

        Assert.IsFalse(ok);
    }

    // -------------------------------------------------------------------------
    // DescribeRejectReason
    // -------------------------------------------------------------------------

    [TestMethod]
    public void DescribeRejectReason_KnownCodes_ReturnNonEmptyStrings()
    {
        byte[] knownCodes =
        [
            TelnetConstants.ReasonConnPartner,
            TelnetConstants.ReasonDeviceInUse,
            TelnetConstants.ReasonInvAssociate,
            TelnetConstants.ReasonInvName,
            TelnetConstants.ReasonInvDeviceType,
            TelnetConstants.ReasonTypeNameError,
            TelnetConstants.ReasonUnknownError,
            TelnetConstants.ReasonUnsupportedReq,
        ];

        foreach (var code in knownCodes)
        {
            var desc = Tn3270EProtocol.DescribeRejectReason(code);
            Assert.IsFalse(string.IsNullOrEmpty(desc), $"Empty description for code {code}");
        }
    }

    [TestMethod]
    public void DescribeRejectReason_UnknownCode_ReturnsHexFallback()
    {
        var desc = Tn3270EProtocol.DescribeRejectReason(0xFE);
        StringAssert.Contains(desc, "UNKNOWN");
    }
}
