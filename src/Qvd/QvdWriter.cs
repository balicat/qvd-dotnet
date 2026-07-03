using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace Qvd;

/// <summary>
/// Writes QlikView / Qlik Sense QVD files.
/// </summary>
/// <remarks>
/// Produces the same layout QlikView writes: XML header, one symbol table per field,
/// bit-packed record section. Distinct values are deduplicated into the symbol tables
/// automatically, bit widths are computed to be as narrow as possible, and NULLs are
/// encoded with the -2 bias convention. All symbol types are supported, including
/// dual values (<see cref="QvdValue.FromDualInteger"/>, <see cref="QvdValue.FromDualDouble"/>,
/// <see cref="QvdValue.FromDateTime"/>).
/// </remarks>
public sealed class QvdWriter
{
    private sealed class FieldColumn
    {
        public FieldColumn(string name) => Name = name;

        public string Name { get; }
        public Dictionary<QvdValue, int> SymbolIndex { get; } = new();
        public List<QvdValue> Symbols { get; } = new();
        public List<int> Rows { get; } = new(); // per-record symbol index; -1 = NULL
        public bool HasNulls { get; set; }
    }

    private readonly List<FieldColumn> _fields;

    public QvdWriter(string tableName, params string[] fieldNames)
    {
        ArgumentException.ThrowIfNullOrEmpty(tableName);
        ArgumentNullException.ThrowIfNull(fieldNames);
        if (fieldNames.Length == 0)
        {
            throw new ArgumentException("At least one field is required.", nameof(fieldNames));
        }

        if (fieldNames.Distinct(StringComparer.Ordinal).Count() != fieldNames.Length)
        {
            throw new ArgumentException("Field names must be unique.", nameof(fieldNames));
        }

        TableName = tableName;
        _fields = fieldNames.Select(n => new FieldColumn(n)).ToList();
    }

    /// <summary>Name of the table to store.</summary>
    public string TableName { get; }

    /// <summary>Written to the header's CreatorDoc element.</summary>
    public string? CreatorDoc { get; set; }

    /// <summary>Header timestamp; defaults to the current UTC time when saving.</summary>
    public DateTime? CreateUtcTime { get; set; }

    /// <summary>Number of records added so far.</summary>
    public int RecordCount { get; private set; }

    /// <summary>
    /// Appends one record: one value per field, in field order.
    /// Use <see cref="QvdValue.Null"/> for NULL.
    /// </summary>
    public void AddRecord(params QvdValue[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length != _fields.Count)
        {
            throw new ArgumentException(
                $"Expected {_fields.Count} values (one per field), got {values.Length}.", nameof(values));
        }

        // Validate everything first so a throw never leaves a half-added record.
        for (int f = 0; f < _fields.Count; f++)
        {
            if (values[f].Text is { } text && text.Contains('\0'))
            {
                throw new ArgumentException(
                    $"Field '{_fields[f].Name}': text values cannot contain NUL characters.", nameof(values));
            }
        }

        for (int f = 0; f < _fields.Count; f++)
        {
            FieldColumn field = _fields[f];
            QvdValue value = values[f];
            if (value.IsNull)
            {
                field.Rows.Add(-1);
                field.HasNulls = true;
                continue;
            }

            if (!field.SymbolIndex.TryGetValue(value, out int index))
            {
                index = field.Symbols.Count;
                field.Symbols.Add(value);
                field.SymbolIndex.Add(value, index);
            }

            field.Rows.Add(index);
        }

        RecordCount++;
    }

    /// <summary>Writes the QVD file to disk.</summary>
    public void Save(string path)
    {
        using var stream = File.Create(path);
        Save(stream);
    }

    /// <summary>Writes the QVD file to a stream. The stream is left open.</summary>
    public void Save(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // --- layout: symbol tables and record bit positions --------------------
        var fieldHeaders = new List<QvdFieldHeader>(_fields.Count);
        using var symbolSection = new MemoryStream();
        int nextBitOffset = 0;

        foreach (FieldColumn field in _fields)
        {
            long offset = symbolSection.Length;
            foreach (QvdValue symbol in field.Symbols)
            {
                EncodeSymbol(symbolSection, symbol);
            }

            // Raw record values are symbol indexes shifted by the bias (nullable fields
            // reserve raw 0 and 1), so the widest raw value decides the bit width.
            int maxRaw = field.Symbols.Count == 0 ? 0 : field.Symbols.Count - 1 + (field.HasNulls ? 2 : 0);
            int bitWidth = BitPacking.BitsNeeded(maxRaw);

            fieldHeaders.Add(new QvdFieldHeader
            {
                FieldName = field.Name,
                BitOffset = nextBitOffset,
                BitWidth = bitWidth,
                Bias = field.HasNulls ? -2 : 0,
                NumberFormat = new QvdNumberFormat { Type = "UNKNOWN", Fmt = "", Dec = "", Thou = "" },
                NoOfSymbols = field.Symbols.Count,
                Offset = offset,
                Length = symbolSection.Length - offset,
                Comment = "",
            });
            nextBitOffset += bitWidth;
        }

        int recordByteSize = Math.Max(1, (nextBitOffset + 7) / 8);

        // --- record section -----------------------------------------------------
        var records = new byte[checked(RecordCount * (long)recordByteSize)];
        for (int f = 0; f < _fields.Count; f++)
        {
            FieldColumn field = _fields[f];
            QvdFieldHeader header = fieldHeaders[f];
            for (int r = 0; r < RecordCount; r++)
            {
                int index = field.Rows[r];
                int raw = index < 0 ? 0 : index - header.Bias;
                BitPacking.Write(records.AsSpan(r * recordByteSize, recordByteSize),
                    header.BitOffset, header.BitWidth, raw);
            }
        }

        // --- XML header ----------------------------------------------------------
        var tableHeader = new QvdTableHeader
        {
            QvBuildNo = "50500",
            CreatorDoc = CreatorDoc ?? "",
            CreateUtcTime = (CreateUtcTime ?? DateTime.UtcNow)
                .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            SourceCreateUtcTime = "",
            SourceFileUtcTime = "",
            StaleUtcTime = "",
            TableName = TableName,
            Fields = fieldHeaders,
            Compression = "",
            RecordByteSize = recordByteSize,
            NoOfRecords = (uint)RecordCount,
            Offset = symbolSection.Length,
            Length = records.Length,
            Comment = "",
        };

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            IndentChars = "  ",
        };
        var namespaces = new XmlSerializerNamespaces();
        namespaces.Add("", "");
        using (var xmlWriter = XmlWriter.Create(stream, settings))
        {
            new XmlSerializer(typeof(QvdTableHeader)).Serialize(xmlWriter, tableHeader, namespaces);
        }

        stream.Write("\r\n"u8);
        stream.WriteByte(0); // NUL terminator: the binary section starts here
        symbolSection.Position = 0;
        symbolSection.CopyTo(stream);
        stream.Write(records);
    }

    private static void EncodeSymbol(Stream target, QvdValue value)
    {
        switch (value.Kind)
        {
            case QvdValueKind.Integer:
                target.WriteByte(0x01);
                WriteInt32(target, (int)value.Number!.Value);
                break;
            case QvdValueKind.Double:
                target.WriteByte(0x02);
                WriteDouble(target, value.Number!.Value);
                break;
            case QvdValueKind.Text:
                target.WriteByte(0x04);
                WriteCString(target, value.Text!);
                break;
            case QvdValueKind.DualInteger:
                target.WriteByte(0x05);
                WriteInt32(target, (int)value.Number!.Value);
                WriteCString(target, value.Text!);
                break;
            case QvdValueKind.DualDouble:
                target.WriteByte(0x06);
                WriteDouble(target, value.Number!.Value);
                WriteCString(target, value.Text!);
                break;
            default:
                throw new InvalidOperationException($"Cannot encode a {value.Kind} value as a symbol.");
        }
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteDouble(Stream stream, double value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteDoubleLittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteCString(Stream stream, string text)
    {
        stream.Write(Encoding.UTF8.GetBytes(text));
        stream.WriteByte(0);
    }
}
