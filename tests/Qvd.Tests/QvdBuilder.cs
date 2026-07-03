using System.Numerics;
using System.Security;
using System.Text;

namespace Qvd.Tests;

/// <summary>
/// Builds a synthetic QVD file in memory so tests don't depend on binary fixtures.
/// This is the writer-side mirror of the format the library reads: XML header,
/// per-field symbol tables, bit-packed record section.
/// </summary>
internal sealed class QvdBuilder
{
    private sealed record FieldSpec(string Name, byte[][] Symbols, int?[] Records, int? ForcedBitWidth);

    private readonly List<FieldSpec> _fields = new();
    private string _tableName = "TestTable";

    public QvdBuilder WithTableName(string name)
    {
        _tableName = name;
        return this;
    }

    /// <summary>
    /// Adds a field. Symbols are pre-encoded with <see cref="Sym"/>; records are per-row
    /// symbol indexes, where null means NULL (the builder applies the -2 bias convention).
    /// </summary>
    public QvdBuilder WithField(string name, byte[][] symbols, int?[] records, int? bitWidth = null)
    {
        _fields.Add(new FieldSpec(name, symbols, records, bitWidth));
        return this;
    }

    public MemoryStream Build()
    {
        if (_fields.Count == 0)
        {
            throw new InvalidOperationException("Add at least one field.");
        }

        int recordCount = _fields[0].Records.Length;
        if (_fields.Any(f => f.Records.Length != recordCount))
        {
            throw new InvalidOperationException("All fields must have the same number of records.");
        }

        // Per-field layout: bias, raw index values, bit width, bit offset.
        var biases = new int[_fields.Count];
        var rawValues = new int[_fields.Count][];
        var bitWidths = new int[_fields.Count];
        var bitOffsets = new int[_fields.Count];

        int nextBitOffset = 0;
        for (int f = 0; f < _fields.Count; f++)
        {
            FieldSpec field = _fields[f];
            bool hasNulls = field.Records.Any(r => r is null);
            biases[f] = hasNulls ? -2 : 0;

            rawValues[f] = field.Records
                .Select(r => hasNulls ? (r is null ? 0 : r.Value + 2) : r ?? 0)
                .ToArray();

            int maxRaw = rawValues[f].Length == 0 ? 0 : rawValues[f].Max();
            bitWidths[f] = field.ForcedBitWidth ?? BitsNeeded(maxRaw);
            bitOffsets[f] = nextBitOffset;
            nextBitOffset += bitWidths[f];
        }

        int recordByteSize = Math.Max(1, (nextBitOffset + 7) / 8);

        // Symbol section: concatenated per-field symbol tables.
        var symbolSection = new MemoryStream();
        var symbolOffsets = new long[_fields.Count];
        var symbolLengths = new long[_fields.Count];
        for (int f = 0; f < _fields.Count; f++)
        {
            symbolOffsets[f] = symbolSection.Length;
            foreach (byte[] symbol in _fields[f].Symbols)
            {
                symbolSection.Write(symbol);
            }

            symbolLengths[f] = symbolSection.Length - symbolOffsets[f];
        }

        // Record section: bit-packed rows.
        var recordSection = new byte[recordCount * recordByteSize];
        for (int r = 0; r < recordCount; r++)
        {
            for (int f = 0; f < _fields.Count; f++)
            {
                WriteBits(recordSection, r * recordByteSize * 8 + bitOffsets[f], bitWidths[f], rawValues[f][r]);
            }
        }

        // XML header.
        var xml = new StringBuilder();
        xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n");
        xml.Append("<QvdTableHeader>\r\n");
        xml.Append("  <QvBuildNo>50500</QvBuildNo>\r\n");
        xml.Append($"  <TableName>{SecurityElement.Escape(_tableName)}</TableName>\r\n");
        xml.Append("  <Fields>\r\n");
        for (int f = 0; f < _fields.Count; f++)
        {
            FieldSpec field = _fields[f];
            xml.Append("    <QvdFieldHeader>\r\n");
            xml.Append($"      <FieldName>{SecurityElement.Escape(field.Name)}</FieldName>\r\n");
            xml.Append($"      <BitOffset>{bitOffsets[f]}</BitOffset>\r\n");
            xml.Append($"      <BitWidth>{bitWidths[f]}</BitWidth>\r\n");
            xml.Append($"      <Bias>{biases[f]}</Bias>\r\n");
            xml.Append("      <NumberFormat><Type>UNKNOWN</Type><nDec>0</nDec><UseThou>0</UseThou></NumberFormat>\r\n");
            xml.Append($"      <NoOfSymbols>{field.Symbols.Length}</NoOfSymbols>\r\n");
            xml.Append($"      <Offset>{symbolOffsets[f]}</Offset>\r\n");
            xml.Append($"      <Length>{symbolLengths[f]}</Length>\r\n");
            xml.Append("      <Tags></Tags>\r\n");
            xml.Append("    </QvdFieldHeader>\r\n");
        }

        xml.Append("  </Fields>\r\n");
        xml.Append($"  <RecordByteSize>{recordByteSize}</RecordByteSize>\r\n");
        xml.Append($"  <NoOfRecords>{recordCount}</NoOfRecords>\r\n");
        xml.Append($"  <Offset>{symbolSection.Length}</Offset>\r\n");
        xml.Append($"  <Length>{recordSection.Length}</Length>\r\n");
        xml.Append("</QvdTableHeader>\r\n");

        var stream = new MemoryStream();
        byte[] xmlBytes = Encoding.UTF8.GetBytes(xml.ToString());
        stream.Write(xmlBytes);
        stream.WriteByte(0); // NUL terminator between header and binary section
        symbolSection.Position = 0;
        symbolSection.CopyTo(stream);
        stream.Write(recordSection);
        stream.Position = 0;
        return stream;
    }

    private static int BitsNeeded(int maxValue) =>
        maxValue == 0 ? 0 : 32 - BitOperations.LeadingZeroCount((uint)maxValue);

    private static void WriteBits(byte[] buffer, int bitOffset, int bitWidth, int value)
    {
        for (int bit = 0; bit < bitWidth; bit++)
        {
            if ((value >> bit & 1) != 0)
            {
                buffer[(bitOffset + bit) >> 3] |= (byte)(1 << ((bitOffset + bit) & 7));
            }
        }
    }
}

/// <summary>Encodes single symbols the way they appear in a QVD symbol table.</summary>
internal static class Sym
{
    public static byte[] Int(int value) =>
        Concat(new byte[] { 0x01 }, BitConverter.GetBytes(value));

    public static byte[] Dbl(double value) =>
        Concat(new byte[] { 0x02 }, BitConverter.GetBytes(value));

    public static byte[] Str(string text) =>
        Concat(new byte[] { 0x04 }, Encoding.UTF8.GetBytes(text), new byte[] { 0 });

    public static byte[] DualInt(int number, string text) =>
        Concat(new byte[] { 0x05 }, BitConverter.GetBytes(number), Encoding.UTF8.GetBytes(text), new byte[] { 0 });

    public static byte[] DualDbl(double number, string text) =>
        Concat(new byte[] { 0x06 }, BitConverter.GetBytes(number), Encoding.UTF8.GetBytes(text), new byte[] { 0 });

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(p => p.Length)];
        int offset = 0;
        foreach (byte[] part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }
}
