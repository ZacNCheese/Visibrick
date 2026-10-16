using System.Text.Json.Serialization;
using SQLite;

namespace Visibrick.Core.Models;

// One catalog item: a minifig, set, part, gear item or book. Minifigs, gear and books come from BrickLink;
// sets and parts come from Rebrickable (see CatalogType.FromRebrickable).
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

    // Item type letter: M, S, P, G or B (see CatalogType)
    [Indexed]
    [JsonPropertyName("item_type")]
    public string Type { get; set; } = "";

    // The item's number from its source, e.g. "sw0001a" (BrickLink), "75192-1" or "3960pr0001" (Rebrickable)
    [JsonPropertyName("number")]
    public string Number { get; set; } = "";

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("category_id")]
    public int? CategoryId { get; set; }

    // e.g. "Star Wars / Star Wars Episode 1"; for Rebrickable sets, the theme path
    [JsonPropertyName("category_name")]
    public string? CategoryName { get; set; }

    // Null when unknown, and always null for parts
    [JsonPropertyName("year_released")]
    public int? YearReleased { get; set; }

    // Other numbers the item is known by (BrickLink parts only), e.g. "71185"
    [JsonPropertyName("alternate_numbers")]
    public string? AlternateNumbers { get; set; }

    [JsonPropertyName("weight_grams")]
    public double? WeightGrams { get; set; }

    // As BrickLink writes it, e.g. "2 x 4 x 4"; null when unknown
    [JsonPropertyName("dimensions")]
    public string? Dimensions { get; set; }

    // Rebrickable sets and parts: the same item's BrickLink number (e.g. part 3960pr0001 is BrickLink 3960pb024),
    // or null when BrickLink doesn't have it
    [JsonPropertyName("bricklink_number")]
    public string? BrickLinkNumber { get; set; }

    // Rebrickable sets and parts: Rebrickable's picture of the item
    [JsonPropertyName("image_url")]
    public string? SourceImageUrl { get; set; }

    // Rebrickable sets: how many pieces it has
    [JsonPropertyName("num_parts")]
    public int? NumParts { get; set; }

    [Ignore]
    [JsonIgnore]
    public bool IsFromRebrickable => CatalogType.FromCode(Type)?.FromRebrickable == true;

    // A small image for cards. Rebrickable items use Rebrickable's picture; BrickLink items use BrickLink's thumbnails.
    [Ignore]
    [JsonIgnore]
    public string ImageUrl => SourceImageUrl ?? (Type == CatalogType.Parts.Code
        ? $"https://img.bricklink.com/ItemImage/PL/{Uri.EscapeDataString(BrickLinkNumber ?? Number)}.png"
        : $"https://img.bricklink.com/ItemImage/{Type}T/0/{Uri.EscapeDataString(Number)}.t2.png");

    // A full-size image for the item's own page (sets and books can be several hundred KB)
    [Ignore]
    [JsonIgnore]
    public string LargeImageUrl => SourceImageUrl ?? (Type == CatalogType.Parts.Code
        ? $"https://img.bricklink.com/ItemImage/PL/{Uri.EscapeDataString(BrickLinkNumber ?? Number)}.png"
        : $"https://img.bricklink.com/ItemImage/{Type}N/0/{Uri.EscapeDataString(Number)}.png");

    // Whether BrickLink has a page for this item (Rebrickable items only when they have a BrickLink number)
    [Ignore]
    [JsonIgnore]
    public bool HasBrickLinkPage => !IsFromRebrickable || BrickLinkNumber is not null;

    // The item's page on BrickLink
    [Ignore]
    [JsonIgnore]
    public string BrickLinkUrl
        => $"https://www.bricklink.com/v2/catalog/catalogitem.page?{Type}={Uri.EscapeDataString(BrickLinkNumber ?? Number)}";

    // The item's page on Rebrickable (sets and parts only)
    [Ignore]
    [JsonIgnore]
    public string? RebrickableUrl => Type switch
    {
        "S" => $"https://rebrickable.com/sets/{Uri.EscapeDataString(Number)}/",
        "P" => $"https://rebrickable.com/parts/{Uri.EscapeDataString(Number)}/",
        _ => null
    };

    // Where tapping the item's number goes: BrickLink when it has the item, otherwise Rebrickable
    [Ignore]
    [JsonIgnore]
    public string LinkUrl => HasBrickLinkPage ? BrickLinkUrl : RebrickableUrl!;

    // The same key as Key, for when you only have the type and number
    public static string MakeKey(string type, string number) => $"{type}:{number}";
}
