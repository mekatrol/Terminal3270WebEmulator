namespace Terminal.Common.Models;

/// <summary>
/// TN3270E data-type byte values as defined in RFC 2355.
/// </summary>
public enum Tn3270EDataType : byte
{
    Data3270 = 0x00,
    ScsData = 0x01,
    Response = 0x02,
    BindImage = 0x03,
    UnbindImage = 0x04,
    NvtData = 0x05,
    Request = 0x06,
    SscpLuData = 0x07,
    PrintEod = 0x08,
}
