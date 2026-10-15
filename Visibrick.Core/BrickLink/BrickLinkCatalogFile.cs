using System.Globalization;
using Visibrick.Core.Models;

namespace Visibrick.Core.BrickLink;

// What a BrickLink catalog download contains, worked out from its file name
// (BrickLink names downloads after the item type, e.g. "Minifigures.txt").
public enum CatalogKind
{
    Minifigs
}

// A BrickLink catalog download (https://www.bricklink.com/catalogDownload.asp, "Tab-Delimited File").
// Layout: a header row, sometimes blank lines, then one item per line with tab-separated columns.
// There's no quoting, and "?" means unknown (e.g. Year Released).
public class BrickLinkCatalogFile
{
    private readonly Dictionary<string, int> _columns;

    private BrickLinkCatalogFile(string fileName, CatalogKind kind, List<string> headers, List<string[]> rows)
    {
        FileName = fileName;
        Kind = kind;
        Headers = headers;
        Rows = rows;
        _columns = headers
            .Select((name, index) => (name, index))
            .GroupBy(c => c.name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().index, StringComparer.OrdinalIgnoreCase);
    }

    public string FileName { get; }

    public CatalogKind Kind { get; }

    public IReadOnlyList<string> Headers { get; }

    public IReadOnlyList<string[]> Rows { get; }

    // Returns null for files that aren't a supported BrickLink download (e.g. a Sets download)
    public static CatalogKind? KindFromFileName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);

        if (name.Contains("minifig", StringComparison.OrdinalIgnoreCase))
            return CatalogKind.Minifigs;

        return null;
    }

    public static async Task<BrickLinkCatalogFile> ReadAsync(string path, CatalogKind kind)
    {
        // BrickLink downloads are UTF-8; a byte-order mark (UTF-8 or UTF-16) is honored if present
        using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);

        var header = await reader.ReadLineAsync() ?? "";
        var headers = header.Split('\t').Select(h => h.Trim()).ToList();
        var rows = new List<string[]>();

        while (await reader.ReadLineAsync() is { } line)
        {
            if (line.Length > 0)
                rows.Add(line.Split('\t'));
        }

        return new BrickLinkCatalogFile(Path.GetFileName(path), kind, headers, rows);
    }

    // All minifigs in the file. Throws FormatException naming the columns it expected if the layout differs.
    public List<LegoMinifig> ParseMinifigs()
    {
        var number = Require("Number");
        var name = Require("Name");
        var categoryId = Find("Category ID");
        var categoryName = Find("Category Name");
        var year = Find("Year Released");
        var weight = Find("Weight (in Grams)", "Weight");

        return Rows.Select(row => new LegoMinifig
        {
            Number = Text(row, number),
            Name = Text(row, name),
            CategoryId = Int(row, categoryId),
            CategoryName = Text(row, categoryName),
            YearReleased = Int(row, year),
            WeightGrams = Double(row, weight)
        })
        .Where(m => !string.IsNullOrEmpty(m.Number))
        .ToList();
    }

    private int Require(params string[] names)
        => Find(names) ?? throw new FormatException(
            $"{FileName}: expected a column named \"{string.Join("\" or \"", names)}\", but the columns are: {string.Join(", ", Headers)}");

    private int? Find(params string[] names)
    {
        foreach (var name in names)
        {
            if (_columns.TryGetValue(name, out var index))
                return index;
        }

        return null;
    }

    private static string? Text(string[] row, int? column)
    {
        if (column is not { } i || i >= row.Length)
            return null;

        var value = row[i].Trim();
        return value.Length == 0 || value == "?" ? null : value;
    }

    private static int? Int(string[] row, int? column)
        => int.TryParse(Text(row, column), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static double? Double(string[] row, int? column)
        => double.TryParse(Text(row, column), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
}
