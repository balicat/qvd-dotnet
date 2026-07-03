using Xunit;

namespace Qvd.Tests;

/// <summary>
/// Round-trip tests: everything the writer produces must decode back identically
/// through the reader. (The reader itself is independently tested against the
/// low-level byte encodings in <see cref="QvdBuilder"/>.)
/// </summary>
public class QvdWriterTests
{
    private static QvdReader RoundTrip(QvdWriter writer)
    {
        var stream = new MemoryStream();
        writer.Save(stream);
        stream.Position = 0;
        return QvdReader.Open(stream);
    }

    [Fact]
    public void RoundTrip_AllValueKinds()
    {
        var writer = new QvdWriter("Blandet_Øl", "i", "d", "t", "di", "dd", "n");
        writer.AddRecord(
            QvdValue.FromInteger(42),
            QvdValue.FromDouble(3.14),
            QvdValue.FromText("Ærøskøbing"),
            QvdValue.FromDualInteger(7, "seven"),
            QvdValue.FromDualDouble(44927.5, "2023-01-01 12:00:00"),
            QvdValue.Null);
        writer.AddRecord(
            QvdValue.FromInteger(-1),
            QvdValue.FromDouble(-0.5),
            QvdValue.FromText(""),
            QvdValue.FromDualInteger(7, "seven"),
            QvdValue.Null,
            QvdValue.FromText("x"));

        var reader = RoundTrip(writer);

        Assert.Equal("Blandet_Øl", reader.TableName);
        Assert.Equal(2, reader.RecordCount);

        QvdValue[] first = reader.GetRecord(0);
        Assert.Equal(QvdValue.FromInteger(42), first[0]);
        Assert.Equal(QvdValue.FromDouble(3.14), first[1]);
        Assert.Equal(QvdValue.FromText("Ærøskøbing"), first[2]);
        Assert.Equal(QvdValue.FromDualInteger(7, "seven"), first[3]);
        Assert.Equal(QvdValue.FromDualDouble(44927.5, "2023-01-01 12:00:00"), first[4]);
        Assert.True(first[5].IsNull);

        QvdValue[] second = reader.GetRecord(1);
        Assert.Equal(QvdValue.FromInteger(-1), second[0]);
        Assert.Equal(QvdValue.FromText(""), second[2]);
        Assert.True(second[4].IsNull);
        Assert.Equal(QvdValue.FromText("x"), second[5]);

        // Duals deduplicate like any other symbol
        Assert.Equal(1, reader.Fields[3].NoOfSymbols);
        Assert.True(reader.Fields[4].HasNulls);
        Assert.True(reader.Fields[5].HasNulls);
        Assert.False(reader.Fields[0].HasNulls);
    }

    [Fact]
    public void RoundTrip_DeduplicatesSymbols_AndUsesNarrowBitWidths()
    {
        var writer = new QvdWriter("Dedup", "flag");
        for (int i = 0; i < 100; i++)
        {
            writer.AddRecord(QvdValue.FromText(i % 2 == 0 ? "yes" : "no"));
        }

        var reader = RoundTrip(writer);
        Assert.Equal(2, reader.Fields[0].NoOfSymbols);
        Assert.Equal(1, reader.Fields[0].BitWidth);
        Assert.Equal("yes", reader.GetRecord(42)[0].Text);
        Assert.Equal("no", reader.GetRecord(43)[0].Text);
    }

    [Fact]
    public void RoundTrip_ConstantField_GetsZeroBitWidth()
    {
        var writer = new QvdWriter("Const", "same", "other");
        writer.AddRecord(QvdValue.FromText("only"), QvdValue.FromInteger(1));
        writer.AddRecord(QvdValue.FromText("only"), QvdValue.FromInteger(2));

        var reader = RoundTrip(writer);
        Assert.Equal(0, reader.Fields[0].BitWidth);
        Assert.Equal("only", reader.GetRecord(1)[0].Text);
        Assert.Equal(2, reader.GetRecord(1)[1].Number);
    }

    [Fact]
    public void RoundTrip_AllNullField()
    {
        var writer = new QvdWriter("Nulls", "empty", "full");
        writer.AddRecord(QvdValue.Null, QvdValue.FromInteger(1));
        writer.AddRecord(QvdValue.Null, QvdValue.FromInteger(2));

        var reader = RoundTrip(writer);
        Assert.Equal(0, reader.Fields[0].NoOfSymbols);
        Assert.True(reader.GetRecord(0)[0].IsNull);
        Assert.True(reader.GetRecord(1)[0].IsNull);
        Assert.Equal(2, reader.GetRecord(1)[1].Number);
    }

    [Fact]
    public void RoundTrip_NoRecords()
    {
        var writer = new QvdWriter("Empty", "a", "b");
        var reader = RoundTrip(writer);
        Assert.Equal(0, reader.RecordCount);
        Assert.Equal(2, reader.Fields.Count);
    }

    [Fact]
    public void RoundTrip_ManyMixedRows()
    {
        var random = new Random(7);
        var writer = new QvdWriter("Mixed", "k", "v", "w");
        var expected = new List<QvdValue[]>();
        for (int i = 0; i < 500; i++)
        {
            var row = new[]
            {
                QvdValue.FromInteger(random.Next(20)),
                random.Next(4) == 0 ? QvdValue.Null : QvdValue.FromDouble(Math.Round(random.NextDouble() * 100, 3)),
                QvdValue.FromDualDouble(40000 + random.Next(1000), $"day-{random.Next(1000)}"),
            };
            expected.Add(row);
            writer.AddRecord(row);
        }

        var reader = RoundTrip(writer);
        Assert.Equal(500, reader.RecordCount);
        for (int i = 0; i < 500; i++)
        {
            Assert.Equal(expected[i], reader.GetRecord(i));
        }
    }

    [Fact]
    public void FromDateTime_ProducesQlikDual()
    {
        QvdValue noon = QvdValue.FromDateTime(new DateTime(2023, 1, 1, 12, 0, 0));
        Assert.Equal(QvdValueKind.DualDouble, noon.Kind);
        Assert.Equal(44927.5, noon.Number); // days since 1899-12-30, noon = .5
        Assert.Equal("2023-01-01 12:00:00", noon.Text);

        QvdValue dateOnly = QvdValue.FromDateTime(new DateTime(2026, 7, 3));
        Assert.Equal("2026-07-03", dateOnly.Text);
        Assert.Equal(46206.0, dateOnly.Number);
    }

    [Fact]
    public void AddRecord_WrongArity_ThrowsAndLeavesWriterUnchanged()
    {
        var writer = new QvdWriter("T", "a", "b");
        Assert.Throws<ArgumentException>(() => writer.AddRecord(QvdValue.FromInteger(1)));
        Assert.Equal(0, writer.RecordCount);

        writer.AddRecord(QvdValue.FromInteger(1), QvdValue.FromInteger(2));
        Assert.Equal(1, writer.RecordCount);
    }

    [Fact]
    public void AddRecord_TextWithNulCharacter_Throws()
    {
        var writer = new QvdWriter("T", "a");
        Assert.Throws<ArgumentException>(() => writer.AddRecord(QvdValue.FromText("bad\0text")));
        Assert.Equal(0, writer.RecordCount);
    }

    [Fact]
    public void Constructor_RejectsDuplicateOrMissingFields()
    {
        Assert.Throws<ArgumentException>(() => new QvdWriter("T"));
        Assert.Throws<ArgumentException>(() => new QvdWriter("T", "a", "a"));
    }
}
