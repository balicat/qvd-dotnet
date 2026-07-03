using System.Globalization;
using System.Text;
using Xunit;

namespace Qvd.Tests;

public class QvdReaderTests
{
    private static QvdReader Open(QvdBuilder builder)
    {
        using MemoryStream stream = builder.Build();
        return QvdReader.Open(stream);
    }

    [Fact]
    public void IntegerSymbols_Decode()
    {
        var reader = Open(new QvdBuilder()
            .WithField("year",
                new[] { Sym.Int(2020), Sym.Int(2021), Sym.Int(2022) },
                new int?[] { 0, 2, 1, 2 }));

        Assert.Equal(4, reader.RecordCount);
        Assert.Equal(QvdValueKind.Integer, reader.GetRecord(0)[0].Kind);
        Assert.Equal(2020, reader.GetRecord(0)[0].Number);
        Assert.Equal(2022, reader.GetRecord(1)[0].Number);
        Assert.Equal(2021, reader.GetRecord(2)[0].Number);
        Assert.Equal("2021", reader.GetRecord(2)[0].DisplayText);
    }

    [Fact]
    public void DoubleSymbols_Decode()
    {
        var reader = Open(new QvdBuilder()
            .WithField("ratio",
                new[] { Sym.Dbl(3.14), Sym.Dbl(-0.5) },
                new int?[] { 0, 1 }));

        Assert.Equal(QvdValueKind.Double, reader.GetRecord(0)[0].Kind);
        Assert.Equal(3.14, reader.GetRecord(0)[0].Number);
        Assert.Equal(-0.5, reader.GetRecord(1)[0].Number);
    }

    [Fact]
    public void StringSymbols_DecodeUtf8()
    {
        var reader = Open(new QvdBuilder()
            .WithField("place",
                new[] { Sym.Str("Ærøskøbing"), Sym.Str("climbing"), Sym.Str("") },
                new int?[] { 0, 1, 2 }));

        Assert.Equal(QvdValueKind.Text, reader.GetRecord(0)[0].Kind);
        Assert.Equal("Ærøskøbing", reader.GetRecord(0)[0].Text);
        Assert.Equal("climbing", reader.GetRecord(1)[0].Text);
        Assert.Equal("", reader.GetRecord(2)[0].Text);
    }

    [Fact]
    public void DualValues_ExposeBothParts()
    {
        var reader = Open(new QvdBuilder()
            .WithField("answer",
                new[] { Sym.DualInt(42, "forty-two") },
                new int?[] { 0 })
            .WithField("when",
                new[] { Sym.DualDbl(44927.5, "2023-01-01 12:00:00") },
                new int?[] { 0 }));

        QvdValue answer = reader.GetRecord(0)[0];
        Assert.Equal(QvdValueKind.DualInteger, answer.Kind);
        Assert.Equal(42, answer.Number);
        Assert.Equal("forty-two", answer.Text);
        Assert.Equal("forty-two", answer.DisplayText);

        QvdValue when = reader.GetRecord(0)[1];
        Assert.Equal(QvdValueKind.DualDouble, when.Kind);
        Assert.Equal(44927.5, when.Number);
        Assert.Equal("2023-01-01 12:00:00", when.Text);
    }

    [Fact]
    public void NullValues_DecodeAsNull()
    {
        var reader = Open(new QvdBuilder()
            .WithField("maybe",
                new[] { Sym.Str("a"), Sym.Str("b") },
                new int?[] { 0, null, 1 }));

        Assert.True(reader.Fields[0].HasNulls);
        Assert.Equal(-2, reader.Fields[0].Bias);

        Assert.Equal("a", reader.GetRecord(0)[0].Text);
        Assert.True(reader.GetRecord(1)[0].IsNull);
        Assert.Equal("b", reader.GetRecord(2)[0].Text);

        Assert.Equal(-2, reader.GetSymbolIndexes(1)[0]);
        Assert.Equal(0, reader.GetSymbolIndexes(0)[0]);
    }

    [Fact]
    public void BitPacking_MultipleFieldsAcrossByteBoundaries()
    {
        // Widths 6 + 2 + 16 = 24 bits; fields straddle byte boundaries like real files do.
        byte[][] years = Enumerable.Range(0, 54).Select(i => Sym.Int(1970 + i)).ToArray();
        byte[][] quarters = Enumerable.Range(1, 4).Select(i => Sym.Int(i)).ToArray();
        byte[][] names = Enumerable.Range(0, 5).Select(i => Sym.Str($"name-{i}")).ToArray();

        var random = new Random(42);
        int rows = 200;
        int?[] yearIdx = Enumerable.Range(0, rows).Select(_ => (int?)random.Next(54)).ToArray();
        int?[] quarterIdx = Enumerable.Range(0, rows).Select(_ => (int?)random.Next(4)).ToArray();
        int?[] nameIdx = Enumerable.Range(0, rows).Select(_ => (int?)random.Next(5)).ToArray();

        var reader = Open(new QvdBuilder()
            .WithField("year", years, yearIdx)
            .WithField("quarter", quarters, quarterIdx)
            .WithField("name", names, nameIdx, bitWidth: 16));

        Assert.Equal(3, reader.Header.RecordByteSize);
        for (int r = 0; r < rows; r++)
        {
            QvdValue[] record = reader.GetRecord(r);
            Assert.Equal(1970 + yearIdx[r]!.Value, record[0].Number);
            Assert.Equal(1 + quarterIdx[r]!.Value, record[1].Number);
            Assert.Equal($"name-{nameIdx[r]!.Value}", record[2].Text);
        }
    }

    [Fact]
    public void ConstantField_WithZeroBitWidth_Decodes()
    {
        var reader = Open(new QvdBuilder()
            .WithField("constant",
                new[] { Sym.Str("always") },
                new int?[] { 0, 0, 0 })
            .WithField("varying",
                new[] { Sym.Int(1), Sym.Int(2) },
                new int?[] { 0, 1, 0 }));

        Assert.Equal(0, reader.Fields[0].BitWidth);
        Assert.Equal("always", reader.GetRecord(2)[0].Text);
        Assert.Equal(2, reader.GetRecord(1)[1].Number);
    }

    [Fact]
    public void NonAsciiHeader_OffsetsRemainCorrect()
    {
        // The XML header must be measured in bytes, not chars: with multi-byte
        // characters in the header these diverge, which shifts every binary offset.
        var reader = Open(new QvdBuilder()
            .WithTableName("Størrelse_π_táblázat")
            .WithField("Værdi",
                new[] { Sym.Str("æøå"), Sym.Int(7) },
                new int?[] { 0, 1 }));

        Assert.Equal("Størrelse_π_táblázat", reader.TableName);
        Assert.Equal("Værdi", reader.Fields[0].FieldName);
        Assert.Equal("æøå", reader.GetRecord(0)[0].Text);
        Assert.Equal(7, reader.GetRecord(1)[0].Number);
    }

    [Fact]
    public void DisplayText_IsCultureInvariant()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE"); // decimal comma culture
            Assert.Equal("3.14", QvdValue.FromDouble(3.14).DisplayText);
            Assert.Equal("-42", QvdValue.FromInteger(-42).DisplayText);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void SymbolTable_ByNameAndIndex()
    {
        var reader = Open(new QvdBuilder()
            .WithField("a", new[] { Sym.Int(1) }, new int?[] { 0 })
            .WithField("b", new[] { Sym.Str("x"), Sym.Str("y") }, new int?[] { 1 }));

        Assert.Equal(2, reader.GetSymbolTable("b").Count);
        Assert.Equal(reader.GetSymbolTable(1), reader.GetSymbolTable("b"));
        Assert.Equal(1, reader.GetFieldIndex("b"));
        Assert.Equal(-1, reader.TryGetFieldIndex("nope"));
        Assert.Throws<ArgumentException>(() => reader.GetSymbolTable("nope"));
    }

    [Fact]
    public void Csv_EscapesSeparatorsQuotesAndNulls()
    {
        var reader = Open(new QvdBuilder()
            .WithField("text",
                new[] { Sym.Str("plain"), Sym.Str("has,comma"), Sym.Str("has\"quote") },
                new int?[] { 0, 1, 2, null }));

        var output = new StringWriter();
        QvdCsv.Write(reader, output);
        string[] lines = output.ToString().Split("\r\n");

        Assert.Equal("text", lines[0]);
        Assert.Equal("plain", lines[1]);
        Assert.Equal("\"has,comma\"", lines[2]);
        Assert.Equal("\"has\"\"quote\"", lines[3]);
        Assert.Equal("", lines[4]); // NULL -> empty cell
    }

    [Fact]
    public void RecordIndex_OutOfRange_Throws()
    {
        var reader = Open(new QvdBuilder()
            .WithField("a", new[] { Sym.Int(1) }, new int?[] { 0 }));

        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetRecord(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetRecord(1));
    }

    [Fact]
    public void ReadRecords_EnumeratesAllInOrder()
    {
        var reader = Open(new QvdBuilder()
            .WithField("n",
                new[] { Sym.Int(10), Sym.Int(20), Sym.Int(30) },
                new int?[] { 2, 0, 1 }));

        double?[] values = reader.ReadRecords().Select(r => r[0].Number).ToArray();
        Assert.Equal(new double?[] { 30, 10, 20 }, values);
    }

    [Fact]
    public void Open_GarbageWithoutXml_Throws()
    {
        using var noXml = new MemoryStream(new byte[] { 1, 2, 3, 0, 9, 9 });
        Assert.Throws<QvdFormatException>(() => QvdReader.Open(noXml));

        using var noTerminator = new MemoryStream(Encoding.UTF8.GetBytes("just some text"));
        Assert.Throws<QvdFormatException>(() => QvdReader.Open(noTerminator));
    }

    [Fact]
    public void Open_TruncatedFile_Throws()
    {
        using MemoryStream full = new QvdBuilder()
            .WithField("a", new[] { Sym.Int(1), Sym.Int(2) }, new int?[] { 0, 1 })
            .Build();

        // Chop off the record section.
        byte[] bytes = full.ToArray();
        using var truncated = new MemoryStream(bytes[..(bytes.Length - 2)]);
        Assert.Throws<QvdFormatException>(() => QvdReader.Open(truncated));
    }
}
