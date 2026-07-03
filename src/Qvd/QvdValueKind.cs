namespace Qvd;

/// <summary>
/// The on-disk symbol type of a decoded QVD value. The numeric members map to the
/// type byte that prefixes each symbol in the file's symbol tables.
/// </summary>
public enum QvdValueKind
{
    /// <summary>No value. Produced by the bit-packed index, not the symbol table (see <see cref="QvdFieldHeader.Bias"/>).</summary>
    Null = 0,

    /// <summary>32-bit signed integer (symbol type byte 0x01).</summary>
    Integer,

    /// <summary>64-bit IEEE 754 double (symbol type byte 0x02).</summary>
    Double,

    /// <summary>UTF-8 text (symbol type byte 0x04).</summary>
    Text,

    /// <summary>Dual value: 32-bit integer plus its display text (symbol type byte 0x05).</summary>
    DualInteger,

    /// <summary>Dual value: double plus its display text, e.g. a date serial and its formatted form (symbol type byte 0x06).</summary>
    DualDouble,
}
