using System.Xml.Serialization;

namespace Qvd;

/// <summary>
/// The XML header that starts every QVD file, deserialized as-is.
/// Everything the binary decoder needs (offsets, bit layout, symbol counts) lives here.
/// </summary>
[XmlRoot("QvdTableHeader")]
public class QvdTableHeader
{
    /// <summary>Build number of the QlikView / Qlik Sense engine that wrote the file.</summary>
    public string? QvBuildNo { get; set; }

    /// <summary>Path of the document that created the file.</summary>
    public string? CreatorDoc { get; set; }

    public string? CreateUtcTime { get; set; }

    public string? SourceCreateUtcTime { get; set; }

    public string? SourceFileUtcTime { get; set; }

    public long SourceFileSize { get; set; } = -1;

    public string? StaleUtcTime { get; set; }

    /// <summary>Name of the table stored in this file.</summary>
    public string TableName { get; set; } = "";

    /// <summary>One header per field, in record layout order.</summary>
    [XmlArray("Fields")]
    [XmlArrayItem("QvdFieldHeader")]
    public List<QvdFieldHeader> Fields { get; set; } = new();

    /// <summary>Compression scheme. Always empty in files observed so far; non-empty values are rejected.</summary>
    public string? Compression { get; set; }

    /// <summary>Size in bytes of one bit-packed record in the data section.</summary>
    public int RecordByteSize { get; set; }

    /// <summary>Number of records (rows) in the table.</summary>
    public uint NoOfRecords { get; set; }

    /// <summary>Byte offset of the record data section, relative to the start of the binary section.</summary>
    public long Offset { get; set; }

    /// <summary>Byte length of the record data section.</summary>
    public long Length { get; set; }

    /// <summary>Load-script lineage: the statements that produced this table.</summary>
    [XmlArray("Lineage")]
    [XmlArrayItem("LineageInfo")]
    public List<QvdLineageInfo> Lineage { get; set; } = new();

    public string? Comment { get; set; }
}

/// <summary>A single lineage entry: where the data came from.</summary>
public class QvdLineageInfo
{
    public string? Discriminator { get; set; }

    public string? Statement { get; set; }
}

/// <summary>Per-field metadata from the XML header.</summary>
public class QvdFieldHeader
{
    public string FieldName { get; set; } = "";

    /// <summary>Bit position of this field's symbol index within a bit-packed record (LSB-first).</summary>
    public int BitOffset { get; set; }

    /// <summary>Number of bits used for this field's symbol index. 0 for constant fields with a single symbol.</summary>
    public int BitWidth { get; set; }

    /// <summary>
    /// Offset added to the raw bit-packed index. 0 for fields without NULLs. -2 for nullable
    /// fields: raw 0 becomes -2 (NULL), raw 1 becomes -1 (also no value), raw n becomes n-2,
    /// an index into the symbol table.
    /// </summary>
    public int Bias { get; set; }

    public QvdNumberFormat? NumberFormat { get; set; }

    /// <summary>Number of distinct values in this field's symbol table.</summary>
    public int NoOfSymbols { get; set; }

    /// <summary>Byte offset of this field's symbol table, relative to the start of the binary section.</summary>
    public long Offset { get; set; }

    /// <summary>Byte length of this field's symbol table.</summary>
    public long Length { get; set; }

    public string? Comment { get; set; }

    /// <summary>Qlik system tags such as $numeric, $integer, $ascii, $text.</summary>
    [XmlArray("Tags")]
    [XmlArrayItem("String")]
    public List<string> Tags { get; set; } = new();

    /// <summary>True when the field can hold NULL values (see <see cref="Bias"/>).</summary>
    [XmlIgnore]
    public bool HasNulls => Bias != 0;
}

/// <summary>Display format hints for a field, as recorded by Qlik.</summary>
public class QvdNumberFormat
{
    /// <summary>UNKNOWN, INTEGER, REAL, FIX, MONEY, DATE, TIME, TIMESTAMP, INTERVAL or ASCII.</summary>
    public string? Type { get; set; }

    [XmlElement("nDec")]
    public int DecimalPlaces { get; set; }

    [XmlElement("UseThou")]
    public int UseThousandsSeparator { get; set; }

    /// <summary>Format pattern, e.g. "YYYY-MM-DD" for dates.</summary>
    public string? Fmt { get; set; }

    /// <summary>Decimal separator.</summary>
    public string? Dec { get; set; }

    /// <summary>Thousands separator.</summary>
    public string? Thou { get; set; }
}
