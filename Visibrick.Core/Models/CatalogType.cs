namespace Visibrick.Core.Models;

// The BrickLink catalog types Visibrick imports. Code is BrickLink's item-type letter, which it also uses
// in image and page addresses (e.g. ?S=75192-1 for a set).
public sealed record CatalogType(string Code, string Name, string CountNoun, string FileKeyword)
{
    public static readonly CatalogType Minifigs = new("M", "Minifigs", "minifigs", "minifig");
    public static readonly CatalogType Sets = new("S", "Sets", "sets", "set");
    public static readonly CatalogType Parts = new("P", "Parts", "parts", "part");
    public static readonly CatalogType Gear = new("G", "Gear", "gear items", "gear");
    public static readonly CatalogType Books = new("B", "Books", "books", "book");

    public static IReadOnlyList<CatalogType> All { get; } = [Minifigs, Sets, Parts, Gear, Books];

    public static CatalogType? FromCode(string? code)
        => All.FirstOrDefault(t => t.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    // BrickLink names its downloads after the item type, e.g. "Minifigures.txt", "Sets.txt", "Parts.txt"
    public static CatalogType? FromFileName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        return All.FirstOrDefault(t => name.Contains(t.FileKeyword, StringComparison.OrdinalIgnoreCase));
    }
}
