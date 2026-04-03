namespace Terminal.Common.Protocol;

public sealed record Tn3270DataStreamDescription(
    byte CommandCode,
    string CommandName,
    byte? Wcc,
    IReadOnlyList<string> Messages);

public static class Tn3270DataStreamParser
{
    public static Tn3270DataStreamDescription Describe(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return new Tn3270DataStreamDescription(0x00, "Empty", null, ["Empty 3270 data stream"]);
        }

        var commandCode = data[0];
        var commandName = GetCommandName(commandCode);
        var messages = new List<string>();

        if (commandCode == 0xF3)
        {
            DescribeStructuredFields(data[1..], messages);
            return new Tn3270DataStreamDescription(commandCode, commandName, null, messages.AsReadOnly());
        }

        if (SupportsWcc(commandCode))
        {
            if (data.Length < 2)
            {
                messages.Add("Missing WCC byte");
                return new Tn3270DataStreamDescription(commandCode, commandName, null, messages.AsReadOnly());
            }

            var wcc = data[1];
            messages.Add($"WCC=0x{wcc:X2}");
            DescribeOrders(data[2..], messages);
            return new Tn3270DataStreamDescription(commandCode, commandName, wcc, messages.AsReadOnly());
        }

        if (data.Length > 1)
        {
            messages.Add($"PayloadLength={data.Length - 1}");
        }

        return new Tn3270DataStreamDescription(commandCode, commandName, null, messages.AsReadOnly());
    }

    private static void DescribeStructuredFields(ReadOnlySpan<byte> data, List<string> messages)
    {
        var offset = 0;
        var fieldIndex = 0;

        while (offset < data.Length)
        {
            if (data.Length - offset < 2)
            {
                messages.Add($"Structured field {fieldIndex}: truncated length header");
                return;
            }

            var length = (data[offset] << 8) | data[offset + 1];
            if (length < 3)
            {
                messages.Add($"Structured field {fieldIndex}: invalid length {length}");
                return;
            }

            if (offset + length > data.Length)
            {
                messages.Add($"Structured field {fieldIndex}: declared length {length} exceeds payload");
                return;
            }

            var id = data[offset + 2];
            messages.Add(
                $"Structured field {fieldIndex}: Id=0x{id:X2} Length={length} DataLength={length - 3}");

            offset += length;
            fieldIndex++;
        }

        if (fieldIndex == 0)
        {
            messages.Add("Write Structured Field command with no structured fields");
        }
    }

    private static void DescribeOrders(ReadOnlySpan<byte> data, List<string> messages)
    {
        var offset = 0;

        while (offset < data.Length)
        {
            var value = data[offset];
            if (!TryGetOrderName(value, out var orderName))
            {
                offset++;
                continue;
            }

            switch (value)
            {
                case 0x1D:
                    if (offset + 1 >= data.Length)
                    {
                        messages.Add("Start Field order truncated before attribute byte");
                        return;
                    }

                    messages.Add($"Field order: {orderName} Attribute=0x{data[offset + 1]:X2}");
                    offset += 2;
                    break;

                case 0x29:
                    if (offset + 1 >= data.Length)
                    {
                        messages.Add("Start Field Extended order truncated before pair count");
                        return;
                    }

                    var pairCount = data[offset + 1];
                    var bytesRequired = 2 + (pairCount * 2);
                    if (offset + bytesRequired > data.Length)
                    {
                        messages.Add(
                            $"Start Field Extended order truncated: PairCount={pairCount} Available={data.Length - offset - 2}");
                        return;
                    }

                    messages.Add($"Field order: {orderName} PairCount={pairCount}");
                    offset += bytesRequired;
                    break;

                case 0x2C:
                    if (offset + 1 >= data.Length)
                    {
                        messages.Add("Modify Field order truncated before pair count");
                        return;
                    }

                    var modifyPairCount = data[offset + 1];
                    var modifyBytesRequired = 2 + (modifyPairCount * 2);
                    if (offset + modifyBytesRequired > data.Length)
                    {
                        messages.Add(
                            $"Modify Field order truncated: PairCount={modifyPairCount} Available={data.Length - offset - 2}");
                        return;
                    }

                    messages.Add($"Field order: {orderName} PairCount={modifyPairCount}");
                    offset += modifyBytesRequired;
                    break;

                case 0x11:
                case 0x3C:
                case 0x12:
                    if (offset + 2 >= data.Length)
                    {
                        messages.Add($"{orderName} order truncated before address bytes");
                        return;
                    }

                    messages.Add(
                        $"Order: {orderName} Operand=0x{data[offset + 1]:X2}{data[offset + 2]:X2}");
                    offset += 3;
                    break;

                case 0x08:
                    if (offset + 1 >= data.Length)
                    {
                        messages.Add("Graphic Escape order truncated before code byte");
                        return;
                    }

                    messages.Add($"Order: {orderName} Code=0x{data[offset + 1]:X2}");
                    offset += 2;
                    break;

                case 0x28:
                    if (offset + 2 >= data.Length)
                    {
                        messages.Add("Set Attribute order truncated before attribute pair");
                        return;
                    }

                    messages.Add(
                        $"Order: {orderName} Type=0x{data[offset + 1]:X2} Value=0x{data[offset + 2]:X2}");
                    offset += 3;
                    break;

                default:
                    messages.Add($"Order: {orderName}");
                    offset++;
                    break;
            }
        }
    }

    private static bool SupportsWcc(byte commandCode) =>
        commandCode is 0xF1 or 0xF5 or 0x7E;

    private static string GetCommandName(byte commandCode) => commandCode switch
    {
        0x01 => "No Operation",
        0x6E => "Read Modified All",
        0x6F => "Erase All Unprotected",
        0x7E => "Erase/Write Alternate",
        0xF1 => "Write",
        0xF2 => "Read Buffer",
        0xF3 => "Write Structured Field",
        0xF5 => "Erase/Write",
        0xF6 => "Read Modified",
        _ => $"Unknown(0x{commandCode:X2})",
    };

    private static bool TryGetOrderName(byte value, out string orderName)
    {
        orderName = value switch
        {
            0x05 => "Program Tab",
            0x08 => "Graphic Escape",
            0x11 => "Set Buffer Address",
            0x12 => "Erase Unprotected to Address",
            0x13 => "Insert Cursor",
            0x1D => "Start Field",
            0x28 => "Set Attribute",
            0x29 => "Start Field Extended",
            0x2C => "Modify Field",
            0x3C => "Repeat to Address",
            _ => string.Empty,
        };

        return orderName.Length > 0;
    }
}
