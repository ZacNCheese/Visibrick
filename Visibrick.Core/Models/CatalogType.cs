namespace Visibrick.Core.Models;

// The catalog types Visibrick has. Code is BrickLink's item-type letter, which it also uses in image and page
// addresses (e.g. ?S=75192-1 for a set). Minifigs, gear and books are imported from BrickLink's catalog
// downloads; sets and parts come from Rebrickable's downloads, which also say what's inside each set.
public sealed record CatalogType(string Code, string Name, string CountNoun, string FileKeyword, bool FromRebrickable = false)
{
    public static readonly CatalogType Minifigs = new("M", "Minifigs", "minifigs", "minifig");
    public static readonly CatalogType Sets = new("S", "Sets", "sets", "set", FromRebrickable: true);
    public static readonly CatalogType Parts = new("P", "Parts", "parts", "part", FromRebrickable: true);
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
