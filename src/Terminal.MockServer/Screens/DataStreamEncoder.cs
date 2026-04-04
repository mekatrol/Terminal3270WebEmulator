namespace Terminal.MockServer.Screens;

/// <summary>
/// Builds a 3270 ERASE WRITE data stream from a <see cref="ScreenDefinition"/>.
/// The resulting bytes are a complete 3270 record payload ready to be wrapped in a TN3270E frame.
/// </summary>
internal static class DataStreamEncoder
{
    // 3270 command codes
    private const byte _commandEraseWrite = 0xF5;

    // Write Control Character: reset MDT and restore the keyboard.
    private const byte _wcc = 0xC2;

    // 3270 order codes
    private const byte _orderSetBufferAddress = 0x11;
    private const byte _orderStartField = 0x1D;
    private const byte _orderInsertCursor = 0x13;

    // Base field-attribute values as interpreted by Terminal.Common:
    // bit 5 = protected, bit 4 = numeric, bits 3-2 = display, bit 0 = MDT.
    // The previous implementation emitted values such as 0x60 and 0x40, which do not match
    // the decoder used by the terminal and produced records that x3270 would accept but not
    // render correctly.
    private const byte _attrProtected = 0x20;
    private const byte _attrProtectedIntensified = 0x28;
    private const byte _attrUnprotected = 0x00;
    private const byte _attrUnprotectedIntensified = 0x08;
    private const byte _attrUnprotectedHidden = 0x0C;
    private const byte _attrUnprotectedNumeric = 0x10;

    // Standard 3270 12-bit buffer-address encoding table (64 characters).
    // address → two encoded bytes using table[(addr >> 6) & 0x3F] + table[addr & 0x3F].
    private static readonly byte[] _addressTable =
    [
        0x40, 0xC1, 0xC2, 0xC3, 0xC4, 0xC5, 0xC6, 0xC7,
        0xC8, 0xC9, 0x4A, 0x4B, 0x4C, 0x4D, 0x4E, 0x4F,
        0x50, 0xD1, 0xD2, 0xD3, 0xD4, 0xD5, 0xD6, 0xD7,
        0xD8, 0xD9, 0x5A, 0x5B, 0x5C, 0x5D, 0x5E, 0x5F,
        0x60, 0x61, 0xE2, 0xE3, 0xE4, 0xE5, 0xE6, 0xE7,
        0xE8, 0xE9, 0x6A, 0x6B, 0x6C, 0x6D, 0x6E, 0x6F,
        0xF0, 0xF1, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7,
        0xF8, 0xF9, 0x7A, 0x7B, 0x7C, 0x7D, 0x7E, 0x7F,
    ];

    // ASCII → EBCDIC CP037 lookup table (indices 0x00–0x7F).
    private static readonly byte[] _asciiToEbcdic =
    [
        0x00, 0x01, 0x02, 0x03, 0x37, 0x2D, 0x2E, 0x2F, // 0x00
        0x16, 0x05, 0x25, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, // 0x08
        0x10, 0x11, 0x12, 0x13, 0x3C, 0x3D, 0x32, 0x26, // 0x10
        0x18, 0x19, 0x3F, 0x27, 0x1C, 0x1D, 0x1E, 0x1F, // 0x18
        0x40, 0x5A, 0x7F, 0x7B, 0x5B, 0x6C, 0x50, 0x7D, // 0x20 ' !"#$%&'
        0x4D, 0x5D, 0x5C, 0x4E, 0x6B, 0x60, 0x4B, 0x61, // 0x28 '()*+,-./'
        0xF0, 0xF1, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, // 0x30 '01234567'
        0xF8, 0xF9, 0x7A, 0x5E, 0x4C, 0x7E, 0x6E, 0x6F, // 0x38 '89:;<=>?'
        0x7C, 0xC1, 0xC2, 0xC3, 0xC4, 0xC5, 0xC6, 0xC7, // 0x40 '@ABCDEFG'
        0xC8, 0xC9, 0xD1, 0xD2, 0xD3, 0xD4, 0xD5, 0xD6, // 0x48 'HIJKLMNO'
        0xD7, 0xD8, 0xD9, 0xE2, 0xE3, 0xE4, 0xE5, 0xE6, // 0x50 'PQRSTUVW'
        0xE7, 0xE8, 0xE9, 0xAD, 0xE0, 0xBD, 0x5F, 0x6D, // 0x58 'XYZ[\\]^_'
        0x79, 0x81, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87, // 0x60 '`abcdefg'
        0x88, 0x89, 0x91, 0x92, 0x93, 0x94, 0x95, 0x96, // 0x68 'hijklmno'
        0x97, 0x98, 0x99, 0xA2, 0xA3, 0xA4, 0xA5, 0xA6, // 0x70 'pqrstuvw'
        0xA7, 0xA8, 0xA9, 0xC0, 0x4F, 0xD0, 0xA1, 0x07, // 0x78 'xyz{|}~⌂'
    ];

    /// <summary>
    /// Encodes <paramref name="screen"/> as a 3270 ERASE WRITE data stream.
    /// </summary>
    /// <returns>
    /// Byte array containing an ERASE WRITE command followed by field orders and EBCDIC text,
    /// ready to be transmitted as the payload of a TN3270E Data3270 frame.
    /// </returns>
    public static byte[] Encode(ScreenDefinition screen)
    {
        var totalCells = screen.Rows * screen.Cols;
        List<byte> stream = [_commandEraseWrite, _wcc];

        var cursorAddr = BufferAddress(screen.Cursor.Row, screen.Cursor.Col, screen.Cols);
        var cursorEmitted = false;

        // Process fields sorted by their content start address so the data stream is written
        // in buffer order, which keeps the cursor-address logic simple.
        var sorted = screen.Fields
            .Select(f => (Field: f, ContentAddr: BufferAddress(f.Row, f.Col, screen.Cols)))
            .OrderBy(x => x.ContentAddr)
            .ToList();

        foreach (var (field, contentAddr) in sorted)
        {
            // The 3270 attribute byte occupies the cell immediately before the content start.
            // When content is at column 1 (address mod cols == 0) the attribute wraps to the
            // last cell of the previous row, which is correct 3270 behaviour.
            var attrAddr = (contentAddr - 1 + totalCells) % totalCells;

            EmitSba(stream, attrAddr);
            stream.Add(_orderStartField);
            stream.Add(AttributeFor(field));

            // If the cursor falls exactly at the first content cell, insert the cursor order here.
            if (!cursorEmitted && contentAddr == cursorAddr)
            {
                stream.Add(_orderInsertCursor);
                cursorEmitted = true;
            }

            var typeLower = field.Type.ToLowerInvariant();
            if (typeLower == "label" && field.Text.Length > 0)
            {
                foreach (var ch in field.Text)
                {
                    stream.Add(ToEbcdic(ch));
                }
            }
            // Input fields emit no content bytes — ERASE WRITE already cleared the buffer to
            // null characters, which the terminal displays as blanks in unprotected fields.
        }

        // If the cursor was not placed inside any field, emit an explicit SBA + IC at the
        // configured cursor position.
        if (!cursorEmitted)
        {
            EmitSba(stream, cursorAddr);
            stream.Add(_orderInsertCursor);
        }

        return [.. stream];
    }

    private static int BufferAddress(int row, int col, int cols) =>
        (row - 1) * cols + (col - 1);

    private static void EmitSba(List<byte> stream, int address)
    {
        stream.Add(_orderSetBufferAddress);
        stream.Add(_addressTable[(address >> 6) & 0x3F]);
        stream.Add(_addressTable[address & 0x3F]);
    }

    private static byte AttributeFor(FieldDefinition field) =>
        field.Type.ToLowerInvariant() switch
        {
            "label" when field.Intensified => _attrProtectedIntensified,
            "label" => _attrProtected,
            "input" when field.Intensified => _attrUnprotectedIntensified,
            "input-hidden" => _attrUnprotectedHidden,
            "input-numeric" => _attrUnprotectedNumeric,
            _ => _attrUnprotected,
        };

    private static byte ToEbcdic(char ch)
    {
        var ascii = (int)ch;
        if (ascii >= 0 && ascii < _asciiToEbcdic.Length)
        {
            return _asciiToEbcdic[ascii];
        }

        // Non-ASCII characters fall back to EBCDIC space (0x40).
        return 0x40;
    }
}
