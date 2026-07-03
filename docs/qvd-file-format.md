# The QVD file format

QVD (QlikView Data) is the proprietary columnar storage format used by QlikView and
Qlik Sense. Qlik has never published a specification. This document describes the
format as reverse engineered from real files, and is what `qvd-dotnet` implements.

## Overall layout

A QVD file has three parts, laid out back to back:

```
┌──────────────────────────────────────────────┐
│ 1. XML header (UTF-8)                        │  table + field metadata
│    ...terminated by a single NUL byte (0x00) │
├──────────────────────────────────────────────┤
│ 2. Symbol tables, one per field              │  the distinct values of each column
├──────────────────────────────────────────────┤
│ 3. Record data                               │  fixed-size bit-packed rows of
│                                              │  symbol-table indexes
└──────────────────────────────────────────────┘
```

All byte offsets in the XML header are relative to the **start of the binary section**
(the byte immediately after the NUL terminator), not to the start of the file.

> **Pitfall:** the header must be measured in *bytes*. If the XML contains multi-byte
> UTF-8 characters (a table name like `Größe`), counting characters instead of bytes
> shifts every offset and corrupts the decode.

## 1. XML header

Plain, human-readable XML (`QvdTableHeader` root). The parts that matter for decoding:

```xml
<QvdTableHeader>
  <TableName>Sales</TableName>
  <Fields>
    <QvdFieldHeader>
      <FieldName>year</FieldName>
      <BitOffset>16</BitOffset>     <!-- where this field's index starts inside a record, in bits -->
      <BitWidth>6</BitWidth>        <!-- how many bits the index uses -->
      <Bias>0</Bias>                <!-- offset added to the raw index (see NULLs below) -->
      <NoOfSymbols>54</NoOfSymbols> <!-- distinct values in the symbol table -->
      <Offset>0</Offset>            <!-- symbol table position, relative to binary section -->
      <Length>270</Length>          <!-- symbol table size in bytes -->
      <NumberFormat><Type>INTEGER</Type> ... </NumberFormat>
      <Tags><String>$numeric</String><String>$integer</String></Tags>
    </QvdFieldHeader>
    ...
  </Fields>
  <RecordByteSize>30</RecordByteSize> <!-- size of one bit-packed record -->
  <NoOfRecords>56758</NoOfRecords>
  <Offset>1084908</Offset>            <!-- record data position, relative to binary section -->
  <Length>1702740</Length>            <!-- record data size in bytes -->
</QvdTableHeader>
```

Other header elements (`CreatorDoc`, `CreateUtcTime`, `Lineage`, ...) are provenance
metadata and not needed to decode the data.

## 2. Symbol tables

QVD stores each column's *distinct values* exactly once, in a per-field symbol table.
Rows then refer to values by index. A symbol table is a byte stream of symbols, each
prefixed with a **type byte**:

| Type byte | Payload | Meaning |
|---|---|---|
| `0x01` | 4 bytes | int32, little-endian |
| `0x02` | 8 bytes | float64 (IEEE 754), little-endian |
| `0x04` | UTF-8 bytes + `0x00` | NUL-terminated string |
| `0x05` | 4 bytes + string + `0x00` | **dual**: int32 followed by its display text |
| `0x06` | 8 bytes + string + `0x00` | **dual**: float64 followed by its display text |

Duals are Qlik's signature value model: one value carrying both a number and a text
representation. Dates and timestamps are the common case — `0x06` with the date serial
number (days since 1899-12-30, time as the fraction of a day) as the double and the
formatted date as the text. `0x05` appears for things like years formatted with
leading text, flags with labels, etc.

Example: the dual `2021 / "2021"` is stored as

```
05 E5 07 00 00 32 30 32 31 00
│  └── 2021 ──┘ └─ "2021" ─┘└─ NUL
└─ type: dual int32 + text
```

## 3. Record data

Records start at header `Offset` (relative to the binary section) and are fixed-size:
`RecordByteSize` bytes each, `NoOfRecords` in total. A record is a bit-packed struct
of per-field symbol indexes:

- Field *f* occupies bits `[BitOffset, BitOffset + BitWidth)` of the record.
- Bit numbering is **little-endian**: bit *k* of the record is bit *(k mod 8)* of byte
  *(k div 8)*. Equivalently: read bytes lowest-first, shift right by `BitOffset mod 8`,
  mask `BitWidth` bits.
- The extracted unsigned integer, plus `Bias`, is the index into the field's symbol table.
- `BitWidth` is exactly as wide as needed: 54 distinct years fit in 6 bits, a constant
  field uses **zero** bits (every row implicitly points at symbol 0).
- Fields are not necessarily laid out in declaration order, and a record may contain
  padding bits that belong to no field.

### NULLs: the bias mechanism

A symbol table has no NULL entry. Instead, a field that contains NULLs gets
`Bias = -2`, and every raw index is shifted:

| Raw bits | + Bias (-2) | Meaning |
|---|---|---|
| 0 | -2 | NULL |
| 1 | -1 | no value (reserved; not observed in practice) |
| n ≥ 2 | n - 2 | symbol table entry n - 2 |

Fields without NULLs have `Bias = 0` and raw indexes are used directly. So after adding
the bias: **negative index → NULL, otherwise index into the symbol table.**

## Decoding walkthrough

To read record *r*, field *f*:

1. Read the XML header up to the NUL byte; parse it. Let `base` = position after the NUL.
2. Slice `RecordByteSize` bytes at `base + Offset + r * RecordByteSize`.
3. Extract `BitWidth(f)` bits at `BitOffset(f)` (little-endian bit order) → `raw`.
4. `index = raw + Bias(f)`. If `index < 0` the value is NULL — done.
5. Otherwise scan field *f*'s symbol table (at `base + Offset(f)`, decoding symbols by
   type byte) and return symbol `index`. In practice you decode the whole symbol table
   once and cache it as an array.

## Notes and edge cases

- **Endianness**: all multi-byte numbers are little-endian.
- **Strings**: UTF-8 in every file observed; QlikView writes the header's declared
  encoding as UTF-8 as well.
- **`Compression`**: the header has a `Compression` element, but it is empty in every
  file QlikView or Qlik Sense produces; the binary sections are never compressed.
- **Zero-bit fields**: a field where every row has the same value has `BitWidth = 0`.
- **Empty strings** are ordinary string symbols (`0x04 0x00`) and are distinct from NULL.
- **Type byte `0x00`** does not exist; a NUL in symbol-table position only ever
  terminates a preceding string.
