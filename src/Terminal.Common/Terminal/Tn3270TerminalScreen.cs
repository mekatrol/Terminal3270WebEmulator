using System.Collections.ObjectModel;
using System.Text;

namespace Terminal.Common.Terminal;

/// <summary>
/// Maintains a mutable 3270 presentation space, including fields, colours, cursor state, and local edits.
/// </summary>
public sealed class Tn3270TerminalScreen
{
    private const byte _commandWrite = 0xF1;
    private const byte _commandEraseWrite = 0xF5;
    private const byte _commandEraseWriteAlternate = 0x7E;
    private const byte _commandEraseAllUnprotected = 0x6F;
    private const byte _orderProgramTab = 0x05;
    private const byte _orderGraphicEscape = 0x08;
    private const byte _orderSetBufferAddress = 0x11;
    private const byte _orderEraseUnprotectedToAddress = 0x12;
    private const byte _orderInsertCursor = 0x13;
    private const byte _orderStartField = 0x1D;
    private const byte _orderSetAttribute = 0x28;
    private const byte _orderStartFieldExtended = 0x29;
    private const byte _orderModifyField = 0x2C;
    private const byte _orderRepeatToAddress = 0x3C;
    private const byte _aidEnter = 0x7D;
    private static readonly byte[] _addressEncodingTable =
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
    private static readonly Dictionary<byte, int> _addressDecodingTable = _addressEncodingTable
        .Select((value, index) => new KeyValuePair<byte, int>(value, index))
        .ToDictionary(static pair => pair.Key, static pair => pair.Value);
    private static readonly Encoding _ebcdicEncoding = CreateEbcdicEncoding();
    private readonly TerminalCell[] _cells;
    private readonly List<TerminalField> _fields = [];
    private readonly Dictionary<int, TerminalField> _fieldAttributes = [];

    /// <summary>
    /// Initialises a screen model for the supplied 3270 terminal type.
    /// </summary>
    public Tn3270TerminalScreen(string terminalType)
        : this(
            GetDimensionsForTerminalType(terminalType).Columns,
            GetDimensionsForTerminalType(terminalType).Rows)
    {
    }

    /// <summary>
    /// Initialises a screen model with the supplied dimensions.
    /// </summary>
    public Tn3270TerminalScreen(int columns, int rows)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columns);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);

        Columns = columns;
        Rows = rows;
        _cells = Enumerable.Range(0, columns * rows)
            .Select(static _ => new TerminalCell())
            .ToArray();

        ClearScreen();
    }

    /// <summary>Gets the negotiated screen width in character cells.</summary>
    public int Columns { get; }

    /// <summary>Gets the negotiated screen height in character cells.</summary>
    public int Rows { get; }

    /// <summary>Gets the linear 3270 cursor address.</summary>
    public int CursorAddress { get; private set; }

    /// <summary>Gets the cells in row-major order.</summary>
    public IReadOnlyList<TerminalCell> Cells => Array.AsReadOnly(_cells);

    /// <summary>Gets the fields currently defined on the screen.</summary>
    public IReadOnlyList<TerminalField> Fields => new ReadOnlyCollection<TerminalField>(_fields);

    /// <summary>
    /// Applies an inbound 3270 write-style record to the presentation space.
    /// </summary>
    public void ApplyInboundRecord(ReadOnlySpan<byte> record)
    {
        if (record.IsEmpty)
        {
            return;
        }

        var command = record[0];
        switch (command)
        {
            case _commandWrite:
            case _commandEraseWrite:
            case _commandEraseWriteAlternate:
                if (record.Length < 2)
                {
                    return;
                }

                ApplyWriteRecord(record[2..], erase: command != _commandWrite);
                break;

            case _commandEraseAllUnprotected:
                EraseAllUnprotected();
                break;
        }
    }

    /// <summary>
    /// Moves the cursor to the next or previous unprotected field.
    /// </summary>
    public bool MoveToAdjacentField(bool forward)
    {
        if (_fields.Count == 0)
        {
            return false;
        }

        var editableFields = _fields
            .Where(static field => !field.IsProtected && field.StartAddress != field.AttributeAddress)
            .OrderBy(static field => field.StartAddress)
            .ToArray();

        if (editableFields.Length == 0)
        {
            return false;
        }

        var currentFieldIndex = GetFieldIndexAt(CursorAddress);
        var orderedIndex = Array.FindIndex(
            editableFields,
            field => _fields.IndexOf(field) == currentFieldIndex);

        var targetIndex = forward
            ? (orderedIndex + 1 + editableFields.Length) % editableFields.Length
            : (orderedIndex - 1 + editableFields.Length) % editableFields.Length;

        if (orderedIndex < 0)
        {
            targetIndex = forward ? 0 : editableFields.Length - 1;
        }

        CursorAddress = editableFields[targetIndex].StartAddress;
        return true;
    }

    /// <summary>
    /// Attempts to move the cursor by one cell in the requested direction while staying on editable cells.
    /// </summary>
    public bool MoveCursor(int deltaColumn, int deltaRow)
    {
        var column = CursorAddress % Columns;
        var row = CursorAddress / Columns;
        var nextColumn = Math.Clamp(column + deltaColumn, 0, Columns - 1);
        var nextRow = Math.Clamp(row + deltaRow, 0, Rows - 1);
        var targetAddress = (nextRow * Columns) + nextColumn;

        if (!CanEdit(targetAddress))
        {
            return false;
        }

        CursorAddress = targetAddress;
        return true;
    }

    /// <summary>
    /// Attempts to write a character at the current cursor location.
    /// </summary>
    public bool TryWriteCharacter(char value)
    {
        if (!CanEdit(CursorAddress))
        {
            return false;
        }

        var fieldIndex = GetFieldIndexAt(CursorAddress);
        if (fieldIndex < 0)
        {
            return false;
        }

        var field = _fields[fieldIndex];
        if (field.IsNumeric && !char.IsDigit(value) && value != ' ')
        {
            return false;
        }

        _cells[CursorAddress].Character = value;
        field.IsModified = true;

        if (CursorAddress != field.EndAddress)
        {
            CursorAddress = Advance(CursorAddress);
        }

        return true;
    }

    /// <summary>
    /// Deletes the previous character inside the current editable field.
    /// </summary>
    public bool Backspace()
    {
        var fieldIndex = GetFieldIndexAt(CursorAddress);
        if (fieldIndex < 0)
        {
            return false;
        }

        var field = _fields[fieldIndex];
        var targetAddress = CursorAddress;

        if (!CanEdit(targetAddress) && CursorAddress > field.StartAddress)
        {
            targetAddress--;
        }
        else if (CanEdit(targetAddress) && CursorAddress > field.StartAddress)
        {
            targetAddress--;
        }

        if (!CanEdit(targetAddress))
        {
            return false;
        }

        _cells[targetAddress].Character = ' ';
        field.IsModified = true;
        CursorAddress = targetAddress;
        return true;
    }

    /// <summary>
    /// Deletes the character at the current cursor location.
    /// </summary>
    public bool Delete()
    {
        if (!CanEdit(CursorAddress))
        {
            return false;
        }

        var fieldIndex = GetFieldIndexAt(CursorAddress);
        if (fieldIndex < 0)
        {
            return false;
        }

        _cells[CursorAddress].Character = ' ';
        _fields[fieldIndex].IsModified = true;
        return true;
    }

    /// <summary>
    /// Builds a Read Modified reply using the local field contents.
    /// </summary>
    public byte[] BuildReadModifiedRecord(byte aid = _aidEnter)
    {
        var payload = new List<byte>(4 + (_fields.Count * 8))
        {
            aid,
        };

        payload.AddRange(EncodeBufferAddress(CursorAddress));

        foreach (var field in _fields.Where(static field => field.IsModified && !field.IsProtected))
        {
            if (field.StartAddress == field.AttributeAddress)
            {
                continue;
            }

            payload.Add(_orderSetBufferAddress);
            payload.AddRange(EncodeBufferAddress(field.StartAddress));

            foreach (var address in EnumerateFieldAddresses(field))
            {
                payload.Add(EncodeCharacter(_cells[address].Character));
            }
        }

        return [.. payload];
    }

    /// <summary>
    /// Returns the row and column for the current cursor position.
    /// </summary>
    public (int Row, int Column) GetCursorCoordinates() =>
        (CursorAddress / Columns, CursorAddress % Columns);

    /// <summary>
    /// Moves the cursor to the first editable cell of the current field.
    /// </summary>
    public bool MoveCursorToFieldStart()
    {
        var fieldIndex = GetFieldIndexAt(CursorAddress);
        if (fieldIndex < 0)
        {
            return false;
        }

        CursorAddress = _fields[fieldIndex].StartAddress;
        return true;
    }

    /// <summary>
    /// Moves the cursor to the last editable cell of the current field.
    /// </summary>
    public bool MoveCursorToFieldEnd()
    {
        var fieldIndex = GetFieldIndexAt(CursorAddress);
        if (fieldIndex < 0)
        {
            return false;
        }

        CursorAddress = _fields[fieldIndex].EndAddress;
        return true;
    }

    private static Encoding CreateEbcdicEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(37);
    }

    private static (int Columns, int Rows) GetDimensionsForTerminalType(string terminalType)
    {
        var normalized = terminalType.Trim().ToUpperInvariant();
        return normalized switch
        {
            "IBM-3278-2" or "IBM-3278-2-E" or "IBM-3279-2" or "IBM-3279-2-E" => (80, 24),
            "IBM-3278-3" or "IBM-3278-3-E" or "IBM-3279-3" or "IBM-3279-3-E" => (80, 32),
            "IBM-3278-4" or "IBM-3278-4-E" or "IBM-3279-4" or "IBM-3279-4-E" => (80, 43),
            "IBM-3278-5" or "IBM-3278-5-E" or "IBM-3279-5" or "IBM-3279-5-E" => (132, 27),
            _ => (80, 24),
        };
    }

    private void ApplyWriteRecord(ReadOnlySpan<byte> orders, bool erase)
    {
        if (erase)
        {
            ClearScreen();
        }

        var address = 0;
        var currentField = new TerminalField
        {
            AttributeAddress = -1,
            StartAddress = 0,
            EndAddress = BufferLength - 1,
            IsProtected = true,
        };
        var foreground = TerminalColor.Default;
        var background = TerminalColor.Default;
        var intensified = false;

        for (var offset = 0; offset < orders.Length; offset++)
        {
            var value = orders[offset];
            switch (value)
            {
                case _orderProgramTab:
                    address = FindNextEditableAddress(address);
                    break;

                case _orderGraphicEscape:
                    offset++;
                    break;

                case _orderSetBufferAddress:
                    if (offset + 2 >= orders.Length)
                    {
                        return;
                    }

                    address = DecodeBufferAddress(orders[offset + 1], orders[offset + 2]);
                    offset += 2;
                    break;

                case _orderEraseUnprotectedToAddress:
                    if (offset + 2 >= orders.Length)
                    {
                        return;
                    }

                    var euaTarget = DecodeBufferAddress(orders[offset + 1], orders[offset + 2]);
                    EraseUnprotectedRange(address, euaTarget);
                    address = euaTarget;
                    offset += 2;
                    break;

                case _orderInsertCursor:
                    CursorAddress = address;
                    break;

                case _orderStartField:
                    if (offset + 1 >= orders.Length)
                    {
                        return;
                    }

                    currentField = CreateFieldFromBaseAttribute(address, orders[offset + 1]);
                    foreground = currentField.Foreground;
                    background = currentField.Background;
                    intensified = currentField.IsIntensified;
                    WriteFieldAttribute(address, currentField);
                    address = Advance(address);
                    offset++;
                    break;

                case _orderSetAttribute:
                    if (offset + 2 >= orders.Length)
                    {
                        return;
                    }

                    ApplyExtendedAttribute(orders[offset + 1], orders[offset + 2], currentField, ref foreground, ref background, ref intensified);
                    offset += 2;
                    break;

                case _orderStartFieldExtended:
                    if (offset + 1 >= orders.Length)
                    {
                        return;
                    }

                    var pairCount = orders[offset + 1];
                    if (offset + 1 + (pairCount * 2) >= orders.Length)
                    {
                        return;
                    }

                    currentField = CreateFieldFromExtendedAttributes(address, orders.Slice(offset + 2, pairCount * 2));
                    foreground = currentField.Foreground;
                    background = currentField.Background;
                    intensified = currentField.IsIntensified;
                    WriteFieldAttribute(address, currentField);
                    address = Advance(address);
                    offset += 1 + (pairCount * 2);
                    break;

                case _orderModifyField:
                    if (offset + 1 >= orders.Length)
                    {
                        return;
                    }

                    var modifyPairCount = orders[offset + 1];
                    if (offset + 1 + (modifyPairCount * 2) >= orders.Length)
                    {
                        return;
                    }

                    ModifyFieldAt(address, orders.Slice(offset + 2, modifyPairCount * 2));
                    offset += 1 + (modifyPairCount * 2);
                    break;

                case _orderRepeatToAddress:
                    if (offset + 3 >= orders.Length)
                    {
                        return;
                    }

                    var target = DecodeBufferAddress(orders[offset + 1], orders[offset + 2]);
                    var repeatedCharacter = DecodeCharacter(orders[offset + 3]);
                    while (address != target)
                    {
                        WriteCharacter(address, repeatedCharacter, currentField, foreground, background, intensified);
                        address = Advance(address);
                    }

                    offset += 3;
                    break;

                default:
                    WriteCharacter(address, DecodeCharacter(value), currentField, foreground, background, intensified);
                    address = Advance(address);
                    break;
            }
        }

        RebuildFieldMembership();
        if (!CanEdit(CursorAddress))
        {
            CursorAddress = FindFirstEditableAddress();
        }
    }

    private void ModifyFieldAt(int address, ReadOnlySpan<byte> pairs)
    {
        if (!_fieldAttributes.TryGetValue(address, out var field))
        {
            return;
        }

        var foreground = field.Foreground;
        var background = field.Background;
        var intensified = field.IsIntensified;

        for (var index = 0; index < pairs.Length; index += 2)
        {
            ApplyExtendedAttribute(pairs[index], pairs[index + 1], field, ref foreground, ref background, ref intensified);
        }

        field.Foreground = foreground;
        field.Background = background;
        field.IsIntensified = intensified;
        WriteFieldAttribute(address, field);
    }

    private void WriteFieldAttribute(int address, TerminalField field)
    {
        _fieldAttributes[address] = field;

        var cell = _cells[address];
        cell.Character = ' ';
        cell.Foreground = field.Foreground;
        cell.Background = field.Background;
        cell.IsProtected = true;
        cell.IsFieldAttribute = true;
        cell.IsHidden = true;
        cell.IsIntensified = field.IsIntensified;
        cell.FieldIndex = -1;
    }

    private static TerminalField CreateFieldFromBaseAttribute(int address, byte attribute)
    {
        var field = new TerminalField
        {
            AttributeAddress = address,
            StartAddress = (address + 1),
            IsProtected = (attribute & 0x20) != 0,
            IsNumeric = (attribute & 0x10) != 0,
            IsModified = (attribute & 0x01) != 0,
            Foreground = TerminalColor.Default,
            Background = TerminalColor.Default,
        };

        var displayBits = attribute & 0x0C;
        field.IsHidden = displayBits == 0x0C;
        field.IsIntensified = displayBits == 0x08;
        return field;
    }

    private static TerminalField CreateFieldFromExtendedAttributes(int address, ReadOnlySpan<byte> pairs)
    {
        var field = new TerminalField
        {
            AttributeAddress = address,
            StartAddress = address + 1,
            Foreground = TerminalColor.Default,
            Background = TerminalColor.Default,
        };

        var foreground = field.Foreground;
        var background = field.Background;
        var intensified = field.IsIntensified;

        for (var index = 0; index < pairs.Length; index += 2)
        {
            var type = pairs[index];
            var value = pairs[index + 1];

            if (type == 0xC0)
            {
                field = CreateFieldFromBaseAttribute(address, value);
                foreground = field.Foreground;
                background = field.Background;
                intensified = field.IsIntensified;
                continue;
            }

            ApplyExtendedAttribute(type, value, field, ref foreground, ref background, ref intensified);
        }

        field.Foreground = foreground;
        field.Background = background;
        field.IsIntensified = intensified;
        return field;
    }

    private static void ApplyExtendedAttribute(
        byte type,
        byte value,
        TerminalField field,
        ref TerminalColor foreground,
        ref TerminalColor background,
        ref bool intensified)
    {
        switch (type)
        {
            case 0x41:
                intensified = value is 0xF2 or 0xF8;
                break;

            case 0x42:
                foreground = MapColor(value);
                break;

            case 0x45:
                background = MapColor(value);
                break;

            case 0x46:
                field.IsHidden = value == 0xF1;
                break;
        }

        field.Foreground = foreground;
        field.Background = background;
        field.IsIntensified = intensified;
    }

    private static TerminalColor MapColor(byte value) => value switch
    {
        0xF1 => TerminalColor.Blue,
        0xF2 => TerminalColor.Red,
        0xF3 => TerminalColor.Pink,
        0xF4 => TerminalColor.Green,
        0xF5 => TerminalColor.Turquoise,
        0xF6 => TerminalColor.Yellow,
        0xF7 => TerminalColor.White,
        0xF8 => TerminalColor.Black,
        0xF9 => TerminalColor.DeepBlue,
        0xFA => TerminalColor.Orange,
        0xFB => TerminalColor.Purple,
        0xFC => TerminalColor.PaleGreen,
        0xFD => TerminalColor.PaleTurquoise,
        0xFE => TerminalColor.Grey,
        _ => TerminalColor.Default,
    };

    private void RebuildFieldMembership()
    {
        _fields.Clear();

        foreach (var cell in _cells)
        {
            cell.FieldIndex = -1;
            if (!cell.IsFieldAttribute)
            {
                cell.IsProtected = true;
                cell.IsHidden = false;
                cell.IsIntensified = false;
                cell.Foreground = TerminalColor.Default;
                cell.Background = TerminalColor.Default;
            }
        }

        var orderedAttributeAddresses = _fieldAttributes.Keys.OrderBy(static address => address).ToArray();
        if (orderedAttributeAddresses.Length == 0)
        {
            for (var index = 0; index < _cells.Length; index++)
            {
                _cells[index].IsProtected = true;
            }

            return;
        }

        for (var index = 0; index < orderedAttributeAddresses.Length; index++)
        {
            var attributeAddress = orderedAttributeAddresses[index];
            var nextAttributeAddress = orderedAttributeAddresses[(index + 1) % orderedAttributeAddresses.Length];
            var field = _fieldAttributes[attributeAddress];
            field.StartAddress = Advance(attributeAddress);
            field.EndAddress = NormalizeAddress(nextAttributeAddress - 1);
            _fields.Add(field);

            var address = field.StartAddress;
            while (true)
            {
                var cell = _cells[address];
                cell.FieldIndex = index;
                cell.IsProtected = field.IsProtected;
                cell.IsHidden = field.IsHidden;
                cell.IsIntensified = field.IsIntensified;
                cell.Foreground = field.Foreground;
                cell.Background = field.Background;

                if (address == field.EndAddress)
                {
                    break;
                }

                address = Advance(address);
            }
        }
    }

    private void WriteCharacter(
        int address,
        char character,
        TerminalField field,
        TerminalColor foreground,
        TerminalColor background,
        bool intensified)
    {
        var cell = _cells[address];
        cell.Character = character;
        cell.Foreground = foreground;
        cell.Background = background;
        cell.IsFieldAttribute = false;
        cell.IsProtected = field.IsProtected;
        cell.IsHidden = field.IsHidden;
        cell.IsIntensified = intensified || field.IsIntensified;
    }

    private void ClearScreen()
    {
        _fieldAttributes.Clear();
        _fields.Clear();
        for (var index = 0; index < _cells.Length; index++)
        {
            _cells[index] = new TerminalCell();
        }

        CursorAddress = 0;
    }

    private void EraseAllUnprotected()
    {
        foreach (var field in _fields.Where(static field => !field.IsProtected))
        {
            foreach (var address in EnumerateFieldAddresses(field))
            {
                _cells[address].Character = ' ';
            }

            field.IsModified = false;
        }

        CursorAddress = FindFirstEditableAddress();
    }

    private void EraseUnprotectedRange(int startAddress, int endAddress)
    {
        var address = startAddress;
        while (address != endAddress)
        {
            if (CanEdit(address))
            {
                _cells[address].Character = ' ';
            }

            address = Advance(address);
        }
    }

    private int FindFirstEditableAddress() => FindNextEditableAddress(BufferLength - 1);

    private int FindNextEditableAddress(int startAddress)
    {
        for (var offset = 1; offset <= BufferLength; offset++)
        {
            var candidate = NormalizeAddress(startAddress + offset);
            if (CanEdit(candidate))
            {
                return candidate;
            }
        }

        return 0;
    }

    private bool CanEdit(int address) =>
        address >= 0
        && address < BufferLength
        && !_cells[address].IsFieldAttribute
        && !_cells[address].IsProtected;

    private int GetFieldIndexAt(int address)
    {
        if (address < 0 || address >= BufferLength)
        {
            return -1;
        }

        return _cells[address].FieldIndex;
    }

    private int Advance(int address) => NormalizeAddress(address + 1);

    private IEnumerable<int> EnumerateFieldAddresses(TerminalField field)
    {
        var address = field.StartAddress;
        while (true)
        {
            yield return address;

            if (address == field.EndAddress)
            {
                yield break;
            }

            address = Advance(address);
        }
    }

    private int NormalizeAddress(int address)
    {
        var result = address % BufferLength;
        return result < 0 ? result + BufferLength : result;
    }

    private int BufferLength => Columns * Rows;

    private static int DecodeBufferAddress(byte first, byte second)
    {
        if ((first & 0xC0) == 0x00)
        {
            return ((first << 8) | second) & 0x3FFF;
        }

        return (DecodeSixBit(first) << 6) | DecodeSixBit(second);
    }

    private static int DecodeSixBit(byte value)
    {
        if (!_addressDecodingTable.TryGetValue(value, out var decoded))
        {
            return 0;
        }

        return decoded;
    }

    private static byte[] EncodeBufferAddress(int address)
    {
        var normalized = address & 0x0FFF;
        return
        [
            _addressEncodingTable[(normalized >> 6) & 0x3F],
            _addressEncodingTable[normalized & 0x3F],
        ];
    }

    private static char DecodeCharacter(byte value) => _ebcdicEncoding.GetString([value])[0];

    private static byte EncodeCharacter(char value)
    {
        var bytes = _ebcdicEncoding.GetBytes([value]);
        return bytes[0];
    }
}
