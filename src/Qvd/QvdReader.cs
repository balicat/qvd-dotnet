using System.Text;
using System.Xml.Serialization;

namespace Qvd;

/// <summary>
/// Reads QlikView / Qlik Sense QVD files.
/// </summary>
/// <remarks>
/// A QVD file is a NUL-terminated XML header followed by a binary section containing
/// one symbol table per field (the distinct values) and a table of bit-packed records
/// (per-field indexes into the symbol tables). See docs/qvd-file-format.md for the
/// full reverse-engineered format description.
///
/// The whole file is buffered in memory; symbol tables are decoded lazily per field
/// and records are decoded on demand, so inspecting a few rows of a large file is cheap.
/// </remarks>
public sealed class QvdReader
{
    private readonly byte[] _data; // the binary section: everything after the XML header's NUL terminator
    private readonly QvdValue[]?[] _symbolTables;
    private readonly long _recordSectionStart;

    private QvdReader(QvdTableHeader header, byte[] data)
    {
        Header = header;
        _data = data;
        _symbolTables = new QvdValue[header.Fields.Count][];
        _recordSectionStart = header.Offset;

        Validate();
    }

    /// <summary>The file's XML header with all raw metadata.</summary>
    public QvdTableHeader Header { get; }

    /// <summary>Name of the stored table.</summary>
    public string TableName => Header.TableName;

    /// <summary>Field metadata, in record layout order.</summary>
    public IReadOnlyList<QvdFieldHeader> Fields => Header.Fields;

    /// <summary>Number of records (rows) in the table.</summary>
    public int RecordCount => checked((int)Header.NoOfRecords);

    /// <summary>Opens and parses a QVD file from disk.</summary>
    public static QvdReader Open(string path)
    {
        using var stream = File.OpenRead(path);
        return Open(stream);
    }

    /// <summary>
    /// Parses a QVD file from a stream. The stream is fully consumed but not disposed.
    /// </summary>
    public static QvdReader Open(Stream stream)
    {
        byte[] headerBytes = ReadHeaderBytes(stream);
        QvdTableHeader header = ParseHeader(headerBytes);

        using var rest = new MemoryStream();
        stream.CopyTo(rest);
        return new QvdReader(header, rest.ToArray());
    }

    /// <summary>
    /// The decoded symbol table (distinct values) for a field. Decoded once, then cached.
    /// </summary>
    public IReadOnlyList<QvdValue> GetSymbolTable(int fieldIndex)
    {
        QvdValue[]? table = _symbolTables[fieldIndex];
        if (table is null)
        {
            table = SymbolTableParser.Parse(_data, Header.Fields[fieldIndex]);
            _symbolTables[fieldIndex] = table;
        }

        return table;
    }

    /// <summary>The decoded symbol table (distinct values) for a named field.</summary>
    public IReadOnlyList<QvdValue> GetSymbolTable(string fieldName) =>
        GetSymbolTable(GetFieldIndex(fieldName));

    /// <summary>Index of a field by name (ordinal comparison), or -1 when absent.</summary>
    public int TryGetFieldIndex(string fieldName)
    {
        for (int i = 0; i < Header.Fields.Count; i++)
        {
            if (string.Equals(Header.Fields[i].FieldName, fieldName, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Index of a field by name; throws when the field does not exist.</summary>
    public int GetFieldIndex(string fieldName)
    {
        int index = TryGetFieldIndex(fieldName);
        if (index < 0)
        {
            throw new ArgumentException($"Field '{fieldName}' does not exist in table '{TableName}'.", nameof(fieldName));
        }

        return index;
    }

    /// <summary>
    /// Decodes one record into values, one per field. NULLs come back as <see cref="QvdValue.Null"/>.
    /// </summary>
    public QvdValue[] GetRecord(int recordIndex)
    {
        int[] indexes = GetSymbolIndexes(recordIndex);
        var values = new QvdValue[indexes.Length];
        for (int f = 0; f < indexes.Length; f++)
        {
            values[f] = indexes[f] < 0 ? QvdValue.Null : GetSymbolTable(f)[indexes[f]];
        }

        return values;
    }

    /// <summary>
    /// The bias-adjusted symbol indexes of one record, one per field.
    /// A negative index means the record holds no value (NULL) for that field.
    /// </summary>
    public int[] GetSymbolIndexes(int recordIndex)
    {
        if (recordIndex < 0 || recordIndex >= RecordCount)
        {
            throw new ArgumentOutOfRangeException(nameof(recordIndex), recordIndex,
                $"Record index must be in [0, {RecordCount}).");
        }

        int start = checked((int)(_recordSectionStart + (long)recordIndex * Header.RecordByteSize));
        ReadOnlySpan<byte> record = _data.AsSpan(start, Header.RecordByteSize);

        var indexes = new int[Header.Fields.Count];
        for (int f = 0; f < indexes.Length; f++)
        {
            QvdFieldHeader field = Header.Fields[f];
            indexes[f] = BitPacking.Extract(record, field.BitOffset, field.BitWidth) + field.Bias;
        }

        return indexes;
    }

    /// <summary>Enumerates all records in order.</summary>
    public IEnumerable<QvdValue[]> ReadRecords()
    {
        for (int i = 0; i < RecordCount; i++)
        {
            yield return GetRecord(i);
        }
    }

    private static byte[] ReadHeaderBytes(Stream stream)
    {
        // The XML header is terminated by a NUL byte; the binary section starts right after.
        // Working in bytes (not chars) keeps the offset correct for non-ASCII headers.
        using var buffer = new MemoryStream();
        int b;
        while ((b = stream.ReadByte()) > 0)
        {
            buffer.WriteByte((byte)b);
        }

        if (b < 0)
        {
            throw new QvdFormatException(
                "Reached end of file before the XML header's NUL terminator. This is probably not a QVD file.");
        }

        return buffer.ToArray();
    }

    private static QvdTableHeader ParseHeader(byte[] headerBytes)
    {
        string xml = Encoding.UTF8.GetString(headerBytes).TrimStart('﻿' /* UTF-8 BOM */);
        if (!xml.TrimStart().StartsWith('<'))
        {
            throw new QvdFormatException("File does not start with an XML header. This is probably not a QVD file.");
        }

        try
        {
            var serializer = new XmlSerializer(typeof(QvdTableHeader));
            using var reader = new StringReader(xml);
            return (QvdTableHeader?)serializer.Deserialize(reader)
                   ?? throw new QvdFormatException("XML header deserialized to nothing.");
        }
        catch (InvalidOperationException ex)
        {
            throw new QvdFormatException($"Malformed QVD XML header: {ex.InnerException?.Message ?? ex.Message}", ex);
        }
    }

    private void Validate()
    {
        if (!string.IsNullOrEmpty(Header.Compression))
        {
            throw new QvdFormatException(
                $"Compressed QVD files are not supported (Compression = '{Header.Compression}').");
        }

        long recordSectionEnd = _recordSectionStart + (long)Header.NoOfRecords * Header.RecordByteSize;
        if (_recordSectionStart < 0 || recordSectionEnd > _data.Length)
        {
            throw new QvdFormatException(
                $"Record section [{_recordSectionStart}..{recordSectionEnd}) does not fit in the binary section ({_data.Length} bytes).");
        }

        int recordBits = Header.RecordByteSize * 8;
        foreach (QvdFieldHeader field in Header.Fields)
        {
            if (field.Offset < 0 || field.Offset + field.Length > _data.Length)
            {
                throw new QvdFormatException(
                    $"Field '{field.FieldName}': symbol table [{field.Offset}..{field.Offset + field.Length}) does not fit in the binary section.");
            }

            if (field.BitWidth is < 0 or > 31)
            {
                throw new QvdFormatException(
                    $"Field '{field.FieldName}': unsupported bit width {field.BitWidth}.");
            }

            if (Header.NoOfRecords > 0 && field.BitOffset + field.BitWidth > recordBits)
            {
                throw new QvdFormatException(
                    $"Field '{field.FieldName}': bit range [{field.BitOffset}..{field.BitOffset + field.BitWidth}) exceeds the {recordBits}-bit record.");
            }
        }
    }
}
