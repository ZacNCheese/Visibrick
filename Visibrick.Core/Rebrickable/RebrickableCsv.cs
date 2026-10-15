using System.Globalization;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using Visibrick.Core.Models;

namespace Visibrick.Core.Rebrickable;

// Reads Rebrickable's daily CSV dumps (https://rebrickable.com/downloads/).
// Shared by the app and the server-side sync so both parse the files the same way.
public static class RebrickableCsv
{
    public const string ColorsUrl = "https://cdn.rebrickable.com/media/downloads/colors.csv.gz";
    public const string MinifigsUrl = "https://cdn.rebrickable.com/media/downloads/minifigs.csv.gz";

    // Streams the rows of a .csv.gz file (header skipped) without writing anything to disk
    public static async IAsyncEnumerable<List<string>> ReadRowsAsync(
        Stream gzipStream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var decompressed = new GZipStream(gzipStream, CompressionMode.Decompress);
        using var reader = new StreamReader(decompressed);

        await reader.ReadLineAsync(cancellationToken); // header

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Length > 0)
                yield return SplitLine(line);
        }
    }

    // Columns: id,name,rgb,is_trans,num_parts,num_sets,y1,y2
    public static LegoColor? ParseColor(List<string> cols)
    {
        if (cols.Count < 4)
            return null;

        return new LegoColor
        {
            Id = int.Parse(cols[0], CultureInfo.InvariantCulture),
            Name = cols[1],
            Rgb = cols[2],
            IsTranslucent = bool.TryParse(cols[3], out bool isTrans) && isTrans
        };
    }

    // Columns: fig_num,name,num_parts,img_url
    public static LegoMinifig? ParseMinifig(List<string> cols)
    {
        if (cols.Count < 4)
            return null;

        return new LegoMinifig
        {
            Number = cols[0],
            Name = cols[1],
            NumParts = int.Parse(cols[2], CultureInfo.InvariantCulture),
            ImageUrl = cols[3]
        };
    }

    // Splits one CSV line, keeping commas inside quoted fields and turning "" into "
    // e.g. fig-000003,"Assassin Droid, White",8,... -> [fig-000003, Assassin Droid, White, 8, ...]
    public static List<string> SplitLine(string line)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (inQuotes)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else
            {
                field.Append(c);
            }
        }

        fields.Add(field.ToString());
        return fields;
    }
}
