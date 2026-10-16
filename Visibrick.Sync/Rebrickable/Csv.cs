using System.IO.Compression;
using System.Text;

namespace Visibrick.Sync.Rebrickable;

// Reads the CSV files in Rebrickable's downloads. Names can contain commas, quotes ("") and line breaks
// inside quoted fields, so a plain Split(',') isn't enough.
public static class Csv
{
    // Every data row of one file in the zip, with its columns looked up by header name
    public static IEnumerable<CsvRow> Rows(ZipArchive zip, string fileName)
    {
        var entry = zip.GetEntry(fileName)
            ?? throw new InvalidDataException($"{fileName} is missing from the zip. Download all the files from rebrickable.com/downloads.");

        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        Dictionary<string, int>? columns = null;

        foreach (var fields in Parse(reader))
        {
            if (columns is null)
            {
                columns = fields.Select((name, i) => (name, i)).ToDictionary(c => c.name, c => c.i);
                continue;
            }

            yield return new CsvRow(fields, columns, fileName);
        }
    }

    private static IEnumerable<string[]> Parse(TextReader reader)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        int c;

        while ((c = reader.Read()) != -1)
        {
            if (quoted)
            {
                if (c == '"')
                {
                    // "" inside quotes is a literal quote; a single " ends the quoted part
                    if (reader.Peek() == '"')
                    {
                        field.Append('"');
                        reader.Read();
                    }
                    else
                        quoted = false;
                }
                else
                    field.Append((char)c);
            }
            else if (c == '"')
                quoted = true;
            else if (c == ',')
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\n')
            {
                fields.Add(field.ToString());
                field.Clear();
                yield return fields.ToArray();
                fields.Clear();
            }
            else if (c != '\r')
                field.Append((char)c);
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            yield return fields.ToArray();
        }
    }
}

public readonly struct CsvRow(string[] fields, Dictionary<string, int> columns, string fileName)
{
    public string this[string column] => columns.TryGetValue(column, out var i) && i < fields.Length
        ? fields[i]
        : throw new InvalidDataException($"{fileName} has no \"{column}\" column. Rebrickable may have changed its file format.");

    public string? Text(string column) => this[column] is { Length: > 0 } value ? value : null;

    public int Int(string column) => int.Parse(this[column]);

    public int? IntOrNull(string column) => int.TryParse(this[column], out var value) ? value : null;

    public bool Bool(string column) => this[column].Equals("True", StringComparison.OrdinalIgnoreCase);
}
