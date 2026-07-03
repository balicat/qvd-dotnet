namespace Qvd;

/// <summary>Writes the decoded contents of a QVD file as CSV (RFC 4180 quoting).</summary>
public static class QvdCsv
{
    /// <summary>
    /// Writes a header row with the field names followed by every record.
    /// Dual values are written as their display text; NULLs as empty cells.
    /// </summary>
    public static void Write(QvdReader reader, TextWriter writer, char separator = ',')
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(writer);

        WriteRow(writer, reader.Fields.Select(f => f.FieldName), separator);
        for (int i = 0; i < reader.RecordCount; i++)
        {
            WriteRow(writer, reader.GetRecord(i).Select(v => v.DisplayText ?? ""), separator);
        }
    }

    private static void WriteRow(TextWriter writer, IEnumerable<string> cells, char separator)
    {
        bool first = true;
        foreach (string cell in cells)
        {
            if (!first)
            {
                writer.Write(separator);
            }

            first = false;
            writer.Write(Escape(cell, separator));
        }

        writer.Write("\r\n");
    }

    private static string Escape(string cell, char separator)
    {
        if (cell.Contains(separator) || cell.Contains('"') || cell.Contains('\n') || cell.Contains('\r'))
        {
            return "\"" + cell.Replace("\"", "\"\"") + "\"";
        }

        return cell;
    }
}
