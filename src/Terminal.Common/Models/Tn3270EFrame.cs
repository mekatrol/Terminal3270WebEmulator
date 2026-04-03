namespace Terminal.Common.Models;

/// <summary>
/// A TN3270E data record received from or sent to the server.
/// Corresponds to the 5-byte TN3270E header (RFC 2355 §4) followed by payload.
/// </summary>
public sealed record Tn3270EFrame(
    Tn3270EDataType DataType,
    byte RequestFlag,
    byte ResponseFlag,
    ushort SequenceNumber,
    ReadOnlyMemory<byte> Data);
