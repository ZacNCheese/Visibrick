using System.Globalization;
using System.Net;
using Visibrick.Core.Models;

namespace Visibrick.Core.BrickLink;

// A BrickLink catalog download (https://www.bricklink.com/catalogDownload.asp, "Tab-Delimited File").
// Layout: a header row, sometimes blank lines, then one item per line with tab-separated columns.
// There's no quoting, "?" means unknown, and a few names contain HTML codes such as &#34; for ".
//
// All item types share Category ID, Category Name, Number, Name and Weight; most add Year Released and
// Dimensions, and Parts has Alternate Item Number instead of a year.
public class BrickLinkCatalogFile
{
    private readonly Dictionary<string, int> _columns;

    private BrickLinkCatalogFile(string fileName, CatalogType type, List<string> headers, List<string[]> rows)
    {
        FileName = fileName;
        Type = type;
        Headers = headers;
        Rows = rows;
        _columns = headers
            .Select((name, index) => (name, index))
            .GroupBy(c => c.name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().index, StringComparer.OrdinalIgnoreCase);
    }

    public string FileName { get; }

    public CatalogType Type { get; }

    public IReadOnlyList<string> Headers { get; }

    public IReadOnlyList<string[]> Rows { get; }

    public static async Task<BrickLinkCatalogFile> ReadAsync(string path, CatalogType type)
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

        return new BrickLinkCatalogFile(Path.GetFileName(path), type, headers, rows);
    }

    // All items in the file. Throws FormatException naming the columns it expected if the layout differs.
    public List<CatalogItem> ParseItems()
    {
        var number = Require("Number");
        var name = Require("Name");
        var categoryId = Find("Category ID");
        var categoryName = Find("Category Name");
        var year = Find("Year Released");
        var alternate = Find("Alternate Item Number");
        var weight = Find("Weight (in Grams)", "Weight");
        var dimensions = Find("Dimensions");

        return Rows
            .Select(row => new CatalogItem
            {
                Type = Type.Code,
                Number = Text(row, number) ?? "",
                Name = Text(row, name),
                CategoryId = Int(row, categoryId),
                CategoryName = Text(row, categoryName),
                YearReleased = Int(row, year),
                AlternateNumbers = Text(row, alternate),
                WeightGrams = Double(row, weight),
                Dimensions = Dimensions(Text(row, dimensions))
            })
            .Where(item => item.Number.Length > 0)
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

        var value = WebUtility.HtmlDecode(row[i]).Trim();
        return value.Length == 0 || value == "?" ? null : value;
    }

    private static int? Int(string[] row, int? column)
        => int.TryParse(Text(row, column), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static double? Double(string[] row, int? column)
        => double.TryParse(Text(row, column), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    // "? x ? x ?" (all unknown) becomes null; partly known values like "2 x 4 x ?" are kept as written
    private static string? Dimensions(string? value)
        => value is null || value.Replace("?", "").Replace("x", "").Trim().Length == 0 ? null : value;
}
