# qvd-dotnet

[![CI](https://github.com/balicat/qvd-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/balicat/qvd-dotnet/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)

A zero-dependency .NET library and command-line tool for reading **QlikView / Qlik Sense QVD files**, based on my reverse engineering of the undocumented binary format.

QVD is Qlik's proprietary columnar storage format. There is no official specification and no official way to read one outside a Qlik product. This project decodes the whole format:

- **All five symbol types** — integers, doubles, strings, and both **dual values** (number + display text in one value, e.g. a date serial together with its formatted text), which many QVD readers skip or flatten.
- **NULL handling** via the format's index *bias* mechanism.
- **Bit-packed records** — rows are stored as little-endian bit-stuffed indexes into per-field symbol tables, at arbitrary bit widths (a year column takes 6 bits per row, a boolean 1 bit).

The full reverse-engineered format description lives in [docs/qvd-file-format.md](docs/qvd-file-format.md).

## Library

```csharp
using Qvd;

var reader = QvdReader.Open("sales.qvd");

Console.WriteLine($"{reader.TableName}: {reader.RecordCount} records");

// Records decode to typed values, not strings
foreach (QvdValue[] record in reader.ReadRecords())
{
    QvdValue amount = record[reader.GetFieldIndex("Amount")];
    if (!amount.IsNull)
    {
        Console.WriteLine($"{amount.Number} (shown as '{amount.DisplayText}')");
    }
}

// Column-oriented access: a field's distinct values without touching the rows
IReadOnlyList<QvdValue> countries = reader.GetSymbolTable("Country");

// Or export everything
using var csv = new StreamWriter("sales.csv");
QvdCsv.Write(reader, csv);
```

`QvdValue` models Qlik's dual semantics directly:

| Kind | `Number` | `Text` | On disk |
|---|---|---|---|
| `Integer` | ✔ | — | `0x01` + int32 |
| `Double` | ✔ | — | `0x02` + float64 |
| `Text` | — | ✔ | `0x04` + UTF-8, NUL-terminated |
| `DualInteger` | ✔ | ✔ | `0x05` + int32 + text |
| `DualDouble` | ✔ | ✔ | `0x06` + float64 + text |
| `Null` | — | — | not in the symbol table (bias) |

Symbol tables are decoded lazily per field and records on demand, so peeking at a few rows of a large file only pays for what you touch.

## CLI

```
$ qvdtool info sales.qvd
File:     sales.qvd
Table:    Sales
Records:  56,758
Fields:   9
Record:   30 bytes, bit-packed

FIELD        TYPE       SYMBOLS  BITS  NULLS  TAGS
year         INTEGER         54     6         $numeric $integer
quarter      INTEGER          4     2         $numeric $integer
country      ASCII           83     7         $text
mtons        REAL         9,741    14  yes    $numeric
...

$ qvdtool head sales.qvd -n 5          # first rows as a table
$ qvdtool csv sales.qvd -o sales.csv   # full export
$ qvdtool symbols sales.qvd country    # a field's distinct values
```

## Build

```
dotnet build
dotnet test
```

Requires the .NET 8 SDK. No runtime dependencies. The test suite builds synthetic QVD files in memory (a writer-side mirror of the format), so no binary fixtures are checked in.

## Scope and limitations

- Reads standard uncompressed QVDs (the only kind QlikView and Qlik Sense write). A non-empty `Compression` header is rejected explicitly rather than misread.
- The file is buffered in memory, so files are limited to ~2 GB.
- Read-only: there is no QVD writer (outside of the in-memory one used by the tests).

## How the format was reverse engineered

QVD files start with a plain XML header that freely gives away table and field metadata — including each field's bit offset, bit width, index bias, and symbol-table extents. The binary section behind it was worked out by inspecting files in a hex editor and cross-checking decoded output against the source tables in QlikView: first the symbol tables with their per-value type bytes, then the bit-stuffed record section, and finally the bias mechanism Qlik uses to represent NULLs as negative indexes. Details in [docs/qvd-file-format.md](docs/qvd-file-format.md).

## Related projects

- [Bondski.QvdLib](https://github.com/Bondski/QvdLib) — .NET QVD reader
- [devinsmith/qvdreader](https://github.com/devinsmith/qvdreader) — C++ QVD reader
- [PyQvd](https://github.com/MuellerConstantin/PyQvd) — Python QVD reader/writer

## License

[MIT](LICENSE) — David Linton
