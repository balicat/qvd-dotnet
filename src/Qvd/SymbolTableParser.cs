using System.Buffers.Binary;
using System.Text;

namespace Qvd;

/// <summary>
/// Decodes one field's symbol table: the list of distinct values, each prefixed
/// by a type byte (0x01 int, 0x02 double, 0x04 string, 0x05/0x06 duals).
/// </summary>
internal static class SymbolTableParser
{
    internal static QvdValue[] Parse(byte[] data, QvdFieldHeader field)
    {
        var symbols = new QvdValue[field.NoOfSymbols];
        int pos = checked((int)field.Offset);
        int end = checked((int)(field.Offset + field.Length));

        if (end > data.Length)
        {
            throw new QvdFormatException(
                $"Field '{field.FieldName}': symbol table extends past the end of the file.");
        }

        for (int s = 0; s < symbols.Length; s++)
        {
            if (pos >= end)
            {
                throw new QvdFormatException(
                    $"Field '{field.FieldName}': symbol table ended after {s} of {field.NoOfSymbols} symbols.");
            }

            byte type = data[pos++];
            switch (type)
            {
                case 0x01: // 4-byte little-endian signed integer
                    symbols[s] = QvdValue.FromInteger(
                        BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos, 4)));
                    pos += 4;
                    break;

                case 0x02: // 8-byte little-endian IEEE 754 double
                    symbols[s] = QvdValue.FromDouble(
                        BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(pos, 8)));
                    pos += 8;
                    break;

                case 0x04: // NUL-terminated UTF-8 string
                    symbols[s] = QvdValue.FromText(ReadCString(data, ref pos, end, field));
                    break;

                case 0x05: // dual: int32 followed by NUL-terminated display text
                {
                    int number = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos, 4));
                    pos += 4;
                    symbols[s] = QvdValue.FromDualInteger(number, ReadCString(data, ref pos, end, field));
                    break;
                }

                case 0x06: // dual: double followed by NUL-terminated display text (dates, timestamps, formatted numbers)
                {
                    double number = BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(pos, 8));
                    pos += 8;
                    symbols[s] = QvdValue.FromDualDouble(number, ReadCString(data, ref pos, end, field));
                    break;
                }

                default:
                    throw new QvdFormatException(
                        $"Field '{field.FieldName}': unknown symbol type byte 0x{type:X2} at binary offset {pos - 1}.");
            }
        }

        return symbols;
    }

    private static string ReadCString(byte[] data, ref int pos, int end, QvdFieldHeader field)
    {
        int terminator = Array.IndexOf(data, (byte)0, pos, end - pos);
        if (terminator < 0)
        {
            throw new QvdFormatException(
                $"Field '{field.FieldName}': unterminated string in symbol table.");
        }

        string value = Encoding.UTF8.GetString(data, pos, terminator - pos);
        pos = terminator + 1;
        return value;
    }
}
