using System.Globalization;
using System.Text;

namespace Qvd.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        try
        {
            return args[0] switch
            {
                "info" => Info(args),
                "head" => Head(args),
                "csv" => Csv(args),
                "symbols" => Symbols(args),
                _ => Fail($"unknown command '{args[0]}'", usage: true),
            };
        }
        catch (ArgumentException ex)
        {
            return Fail(ex.Message, usage: true);
        }
        catch (QvdFormatException ex)
        {
            return Fail(ex.Message);
        }
        catch (IOException ex)
        {
            return Fail(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Fail(ex.Message);
        }
    }

    // ----- commands ---------------------------------------------------------

    private static int Info(string[] args)
    {
        var (file, _, _) = ParseCommandArgs(args);
        var reader = QvdReader.Open(file);
        var header = reader.Header;

        Console.WriteLine($"File:     {file}");
        Console.WriteLine($"Table:    {reader.TableName}");
        Console.WriteLine($"Records:  {N(reader.RecordCount)}");
        Console.WriteLine($"Fields:   {reader.Fields.Count}");
        Console.WriteLine($"Record:   {header.RecordByteSize} bytes, bit-packed");
        if (!string.IsNullOrEmpty(header.CreateUtcTime))
        {
            Console.WriteLine($"Created:  {header.CreateUtcTime} UTC");
        }

        if (!string.IsNullOrEmpty(header.CreatorDoc))
        {
            Console.WriteLine($"Creator:  {header.CreatorDoc}");
        }

        Console.WriteLine();

        var rows = new List<string[]>
        {
            new[] { "FIELD", "TYPE", "SYMBOLS", "BITS", "NULLS", "TAGS" },
        };
        rows.AddRange(reader.Fields.Select(f => new[]
        {
            f.FieldName,
            f.NumberFormat?.Type ?? "-",
            N(f.NoOfSymbols),
            f.BitWidth.ToString(CultureInfo.InvariantCulture),
            f.HasNulls ? "yes" : "",
            string.Join(" ", f.Tags),
        }));
        PrintTable(rows);
        return 0;
    }

    private static int Head(string[] args)
    {
        var (file, options, _) = ParseCommandArgs(args);
        int count = GetIntOption(options, "-n", 10);
        var reader = QvdReader.Open(file);
        count = Math.Clamp(count, 0, reader.RecordCount);

        var rows = new List<string[]>
        {
            reader.Fields.Select(f => f.FieldName).ToArray(),
        };
        for (int i = 0; i < count; i++)
        {
            rows.Add(reader.GetRecord(i).Select(v => v.DisplayText ?? "").ToArray());
        }

        PrintTable(rows);
        Console.WriteLine();
        Console.WriteLine($"({N(count)} of {N(reader.RecordCount)} records)");
        return 0;
    }

    private static int Csv(string[] args)
    {
        var (file, options, _) = ParseCommandArgs(args);
        char separator = GetSeparatorOption(options);
        var reader = QvdReader.Open(file);

        if (options.TryGetValue("-o", out string? outputPath))
        {
            using var writer = new StreamWriter(outputPath, append: false, Encoding.UTF8);
            QvdCsv.Write(reader, writer, separator);
            Console.WriteLine($"Wrote {N(reader.RecordCount)} records to {outputPath}");
        }
        else
        {
            QvdCsv.Write(reader, Console.Out, separator);
        }

        return 0;
    }

    private static int Symbols(string[] args)
    {
        var (file, options, positionals) = ParseCommandArgs(args);
        if (positionals.Count < 2)
        {
            throw new ArgumentException("symbols needs a field name: qvdtool symbols <file.qvd> <field>");
        }

        int count = GetIntOption(options, "-n", 20);
        var reader = QvdReader.Open(file);
        var table = reader.GetSymbolTable(positionals[1]);
        int shown = count <= 0 ? table.Count : Math.Min(count, table.Count);

        var rows = new List<string[]>
        {
            new[] { "INDEX", "KIND", "NUMBER", "TEXT" },
        };
        for (int i = 0; i < shown; i++)
        {
            var value = table[i];
            rows.Add(new[]
            {
                i.ToString(CultureInfo.InvariantCulture),
                value.Kind.ToString(),
                value.Number?.ToString("R", CultureInfo.InvariantCulture) ?? "",
                value.Text ?? "",
            });
        }

        PrintTable(rows);
        Console.WriteLine();
        Console.WriteLine(shown < table.Count
            ? $"({N(shown)} of {N(table.Count)} symbols; use -n 0 to show all)"
            : $"({N(table.Count)} symbols)");
        return 0;
    }

    // ----- argument parsing -------------------------------------------------

    private static (string File, Dictionary<string, string> Options, List<string> Positionals) ParseCommandArgs(string[] args)
    {
        var options = new Dictionary<string, string>();
        var positionals = new List<string>();

        for (int i = 1; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg.StartsWith('-'))
            {
                if (i + 1 >= args.Length)
                {
                    throw new ArgumentException($"option {arg} needs a value");
                }

                options[arg] = args[++i];
            }
            else
            {
                positionals.Add(arg);
            }
        }

        if (positionals.Count == 0)
        {
            throw new ArgumentException("missing <file.qvd> argument");
        }

        return (positionals[0], options, positionals);
    }

    private static int GetIntOption(Dictionary<string, string> options, string name, int defaultValue)
    {
        if (!options.TryGetValue(name, out string? raw))
        {
            return defaultValue;
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            throw new ArgumentException($"option {name} expects a number, got '{raw}'");
        }

        return value;
    }

    private static char GetSeparatorOption(Dictionary<string, string> options)
    {
        if (!options.TryGetValue("-s", out string? raw))
        {
            return ',';
        }

        return raw switch
        {
            "\\t" or "tab" => '\t',
            { Length: 1 } => raw[0],
            _ => throw new ArgumentException($"option -s expects a single character (or 'tab'), got '{raw}'"),
        };
    }

    // ----- output helpers ----------------------------------------------------

    private const int MaxCellWidth = 40;

    private static void PrintTable(List<string[]> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        int columns = rows[0].Length;
        var widths = new int[columns];
        foreach (string[] row in rows)
        {
            for (int c = 0; c < columns; c++)
            {
                widths[c] = Math.Min(Math.Max(widths[c], row[c].Length), MaxCellWidth);
            }
        }

        foreach (string[] row in rows)
        {
            var line = new StringBuilder();
            for (int c = 0; c < columns; c++)
            {
                string cell = row[c].Length > MaxCellWidth ? row[c][..(MaxCellWidth - 3)] + "..." : row[c];
                line.Append(cell.PadRight(widths[c]));
                if (c < columns - 1)
                {
                    line.Append("  ");
                }
            }

            Console.WriteLine(line.ToString().TrimEnd());
        }
    }

    private static string N(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static int Fail(string message, bool usage = false)
    {
        Console.Error.WriteLine($"error: {message}");
        if (usage)
        {
            Console.Error.WriteLine();
            PrintUsage(Console.Error);
        }

        return usage ? 1 : 2;
    }

    private static void PrintUsage(TextWriter? writer = null)
    {
        (writer ?? Console.Out).Write(
            """
            qvdtool - inspect and export QlikView/Qlik Sense QVD files

            usage:
              qvdtool info    <file.qvd>                     table and field metadata
              qvdtool head    <file.qvd> [-n rows]           first rows as a table (default 10)
              qvdtool csv     <file.qvd> [-o out.csv] [-s ;] export all records as CSV
              qvdtool symbols <file.qvd> <field> [-n count]  a field's distinct values (default 20, 0 = all)

            examples:
              qvdtool info sales.qvd
              qvdtool head sales.qvd -n 25
              qvdtool csv sales.qvd -o sales.csv
              qvdtool csv sales.qvd -s tab > sales.tsv
              qvdtool symbols sales.qvd Country -n 0

            """);
    }
}
