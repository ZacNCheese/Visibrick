using System.Text.Json.Serialization;
using SQLite;

namespace Visibrick.Core.Models;

// One item from BrickLink's catalog: a minifig, set, part, gear item or book.
// JSON names match the Supabase "catalog_items" table.
public class CatalogItem
{
    // Primary key for the app's local SQLite copy. Numbers are only unique within a type
    // (a set and a book can share a number), so the key combines both, e.g. "S:75192-1".
    // SQLite needs a setter to load rows; the key is always worked out from Type and Number.
    [PrimaryKey]
    [JsonIgnore]
    public string Key
    {
        get => $"{Type}:{Number}";
        set { }
    }

    // BrickLink item type letter: M, S, P, G or B (see CatalogType)
    [Indexed]
    [JsonPropertyName("item_type")]
    public string Type { get; set; } = "";

    // BrickLink item number, e.g. "sw0001a", "75192-1", "3001"
    [JsonPropertyName("number")]
    public string Number { get; set; } = "";

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("category_id")]
    public int? CategoryId { get; set; }

    // e.g. "Star Wars / Star Wars Episode 1"
    [JsonPropertyName("category_name")]
    public string? CategoryName { get; set; }

    // Null when BrickLink lists the year as unknown ("?"), and always null for parts
    [JsonPropertyName("year_released")]
    public int? YearReleased { get; set; }

    // Other numbers the item is known by (parts only), e.g. "71185"
    [JsonPropertyName("alternate_numbers")]
    public string? AlternateNumbers { get; set; }

    [JsonPropertyName("weight_grams")]
    public double? WeightGrams { get; set; }

    // As BrickLink writes it, e.g. "2 x 4 x 4"; null when unknown
    [JsonPropertyName("dimensions")]
    public string? Dimensions { get; set; }

    // A small image from BrickLink. Parts are shown in a default color; everything else uses BrickLink's thumbnails.
    [Ignore]
    [JsonIgnore]
    public string ImageUrl => Type == CatalogType.Parts.Code
        ? $"https://img.bricklink.com/ItemImage/PL/{Uri.EscapeDataString(Number)}.png"
        : $"https://img.bricklink.com/ItemImage/{Type}T/0/{Uri.EscapeDataString(Number)}.t2.png";

    // A full-size image for the item's own page (sets and books can be several hundred KB)
    [Ignore]
    [JsonIgnore]
    public string LargeImageUrl => Type == CatalogType.Parts.Code
        ? $"https://img.bricklink.com/ItemImage/PL/{Uri.EscapeDataString(Number)}.png"
        : $"https://img.bricklink.com/ItemImage/{Type}N/0/{Uri.EscapeDataString(Number)}.png";

    // The item's page on BrickLink
    [Ignore]
    [JsonIgnore]
    public string BrickLinkUrl => $"https://www.bricklink.com/v2/catalog/catalogitem.page?{Type}={Uri.EscapeDataString(Number)}";

    // The same key as Key, for when you only have the type and number
    public static string MakeKey(string type, string number) => $"{type}:{number}";
}
