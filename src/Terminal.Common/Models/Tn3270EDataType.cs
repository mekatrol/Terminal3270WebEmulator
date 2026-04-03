namespace Terminal.Common.Models;

/// <summary>
/// TN3270E data-type byte values as defined in RFC 2355.
/// </summary>
public enum Tn3270EDataType : byte
{
    /// <summary>
    /// 3270 data stream record.
    /// </summary>
    Data3270 = 0x00,

    /// <summary>
    /// SNA Character String data.
    /// </summary>
    ScsData = 0x01,

    /// <summary>
    /// Positive or negative response message.
    /// </summary>
    Response = 0x02,

    /// <summary>
    /// BIND image data for session setup.
    /// </summary>
    BindImage = 0x03,

    /// <summary>
    /// UNBIND image data for session teardown.
    /// </summary>
    UnbindImage = 0x04,

    /// <summary>
    /// NVT data for line-mode or character-mode terminal traffic.
    /// </summary>
    NvtData = 0x05,

    /// <summary>
    /// Request message requiring a TN3270E response.
    /// </summary>
    Request = 0x06,

    /// <summary>
    /// SSCP-LU data stream record.
    /// </summary>
    SscpLuData = 0x07,

    /// <summary>
    /// Printer end-of-data marker.
    /// </summary>
    PrintEod = 0x08,
}
